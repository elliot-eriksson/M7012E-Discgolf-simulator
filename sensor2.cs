using UnityEngine;
using System;
using System.IO.Ports;
using System.Collections.Generic;
using System.Threading;
using System.IO;


public class sensor2 : MonoBehaviour
{
    public float Roll { get; private set; }
    public float Pitch { get; private set; }
    public float Yaw { get; private set; }
    private float estimatedTimestamp = 0f;
    private float updateInterval = 1f / 200f;
    [SerializeField] private string portName = "COM5";
    [SerializeField] private int baudRate = 115200;

    // We'll handle stable flight in THROWN via a time delay + minimal change checks
    [SerializeField] private float stableDelay = 0.2f;      // NEW: how many seconds to wait after a throw is detected
    private float timeSinceThrown = 0f;                    // NEW: how long we've been in THROWN
    private bool finalCenterSet = false;                   // NEW: have we computed the final center after the delay?

    // For minimal change detection
    [SerializeField] private float changeThreshold = 100f;  // how many deg/s difference is considered "small"?
    private int stableDeltaCounter = 0;                    // how many consecutive frames had a small Δwz?

    // Step 1: Throw detection states
    private enum ThrowState { IDLE, THROWN, IN_FLIGHT, DONE }
    private ThrowState currentThrowState = ThrowState.IDLE;

    [SerializeField] private float throwThreshold = 600f;       // For crossing from IDLE -> THROWN
    [SerializeField] private int thresholdSamplesNeeded = 3;   // # of consecutive frames above throwThreshold
    private int thresholdCounter = 0;

    // We'll capture the rotation at the moment of throw
    private float dynamicCenter = 0f;

    // Used once we're in THROWN
    [SerializeField] private float stableBand = 200f;
    [SerializeField] private int stableSamplesNeeded = 3;
    private int stableCounter = 0;

    // Used once we're in IN_FLIGHT
    [SerializeField] private float nearZeroThreshold = 150f;
    [SerializeField] private int nearZeroSamplesNeeded = 2;
    private int nearZeroCounter = 0;

    private float throwStartTime = -1f;
    private float flightEndTime = -1f;

    // --------------------------------------------------------------------------------
    // RING BUFFER to track recent samples (time + wz) so we can retroactively find
    // the start of the throw when we confirm thresholdSamplesNeeded consecutive frames.
    // --------------------------------------------------------------------------------
    private struct FrameSample
    {
        public float time;
        public float wz;
    }

    // You can store more frames if you want (e.g. 30-60) so you have some history
    private const int RING_BUFFER_CAPACITY = 30;
    private List<FrameSample> ringBuffer = new List<FrameSample>(RING_BUFFER_CAPACITY);

    // Existing fields...
    private SerialPort _serialPort;
    private Thread _readThread;
    private bool _keepReading = false;
    private StreamWriter _csvWriter;
    private Dictionary<string, double> lastData = null;
    private float throwTimestamp = 0f;
    private float maxWz = 0f;
    private float throwDetectedTime = -1f;
    private Vector3 velocity = Vector3.zero;

    void Start()
    {
        Application.targetFrameRate = 30;
        estimatedTimestamp = Time.time;

        // 1) Open or create a CSV file.
        string csvPath = Application.dataPath + "/BWT901_log_" +
                  DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

        try
        {
            _csvWriter = new StreamWriter(csvPath, false);
            _csvWriter.WriteLine("Timestamp;ax;ay;az;wx;wy;wz;Roll;Pitch;Yaw;Vx;Vy;Vz;Speed");
            _csvWriter.Flush();
            Debug.Log("CSV file opened at: " + csvPath);
        }
        catch (Exception e)
        {
            Debug.LogError("Could not open CSV file for writing: " + e.Message);
        }

        // 2) Initialize and open the serial port.
        _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
        _serialPort.ReadTimeout = 500;
        _serialPort.Handshake = Handshake.None;
        _serialPort.DtrEnable = true;
        _serialPort.RtsEnable = true;

        try
        {
            _serialPort.Open();
            Debug.Log("Serial port opened successfully.");

            // 3) Start a background thread to read the sensor data.
            _keepReading = true;
            _readThread = new Thread(ReadDataLoop);
            _readThread.Start();
        }
        catch (Exception e)
        {
            Debug.LogError("Error opening serial port: " + e.Message);
        }
    }

    void Update()
    {
        if (lastData == null) return;

        estimatedTimestamp += Time.deltaTime;
        float currentWz = (float)lastData["wz"];

        // 1) Add current sample to ringBuffer
        FrameSample sample = new FrameSample { time = estimatedTimestamp, wz = currentWz };
        ringBuffer.Add(sample);
        if (ringBuffer.Count > RING_BUFFER_CAPACITY)
            ringBuffer.RemoveAt(0);

        switch (currentThrowState)
        {
            case ThrowState.IDLE:
                {
                    // Count how many consecutive frames from the end are above throwThreshold
                    int consecutiveCount = 0;
                    for (int i = ringBuffer.Count - 1; i >= 0; i--)
                    {
                        if (Mathf.Abs(ringBuffer[i].wz) >= throwThreshold)
                            consecutiveCount++;
                        else
                            break;
                    }
                    if (consecutiveCount >= thresholdSamplesNeeded)
                    {
                        currentThrowState = ThrowState.THROWN;
                        timeSinceThrown = 0f;        // NEW: start our timer
                        finalCenterSet = false;      // NEW: we haven't computed final center yet

                        // Mark throw start time retroactively
                        int earliestIndex = ringBuffer.Count - consecutiveCount;
                        throwStartTime = ringBuffer[earliestIndex].time;

                        // Temporary center (just use last sample for now)
                        dynamicCenter = ringBuffer[ringBuffer.Count - 1].wz;
                        Debug.Log($"[STATE] Throw DETECTED at {throwStartTime:F3}, temporaryCenter={dynamicCenter:F2}");
                    }
                }
                break;

            case ThrowState.THROWN:
                {
                    timeSinceThrown += Time.deltaTime;

                    // If we haven't set our final center yet and the stableDelay has passed,
                    // compute dynamicCenter from the last few frames
                    if (!finalCenterSet && timeSinceThrown >= stableDelay)
                    {
                        finalCenterSet = true;  // only do this once

                        // For example, average the last 5 frames
                        int framesToAvg = Mathf.Min(5, ringBuffer.Count);
                        float sum = 0f;
                        for (int i = ringBuffer.Count - framesToAvg; i < ringBuffer.Count; i++)
                            sum += ringBuffer[i].wz;
                        dynamicCenter = sum / framesToAvg;
                        Debug.Log($"[THROWN] Final center set to {dynamicCenter:F2} after delay");
                    }

                    // Once we have a final center, we do minimal-change checks:
                    // i.e., if consecutive frames differ by < changeThreshold from one update to the next.
                    if (finalCenterSet && ringBuffer.Count > 1)
                    {
                        // Compare the last 2 frames
                        float wzCurr = ringBuffer[ringBuffer.Count - 1].wz;
                        float wzPrev = ringBuffer[ringBuffer.Count - 2].wz;
                        float delta = Mathf.Abs(wzCurr - wzPrev);

                        if (delta < changeThreshold)
                        {
                            stableDeltaCounter++;
                        }
                        else
                        {
                            stableDeltaCounter = 0;
                        }

                        if (stableDeltaCounter >= stableSamplesNeeded)
                        {
                            currentThrowState = ThrowState.IN_FLIGHT;
                            Debug.Log($"[STATE] Stable flight at {estimatedTimestamp:F3}, center~{dynamicCenter:F2}");
                        }
                    }

                    // (Optional) If you want a partial “abort” if rotation dips below threshold for multiple frames, add that here
                }
                break;

            case ThrowState.IN_FLIGHT:
                {
                    // If rotation dips below nearZeroThreshold for enough frames, we're done
                    float absWz = Mathf.Abs(currentWz);
                    if (absWz < nearZeroThreshold)
                    {
                        nearZeroCounter++;
                        if (nearZeroCounter >= nearZeroSamplesNeeded)
                        {
                            currentThrowState = ThrowState.DONE;
                            flightEndTime = estimatedTimestamp;
                            Debug.Log($"[STATE] Flight ended at {flightEndTime:F3}");
                        }
                    }
                    else
                    {
                        nearZeroCounter = 0;
                    }
                }
                break;

            case ThrowState.DONE:
                // Possibly do nothing or revert to IDLE if you want multiple throws
                break;
        }

        //Debug.Log(ringBuffer);

        // Save to CSV (unchanged)
        _csvWriter.WriteLine(
            $"{estimatedTimestamp:F3};" +
            $"{lastData["ax"]:F3};{lastData["ay"]:F3};{lastData["az"]:F3};" +
            $"{lastData["wx"]:F3};{lastData["wy"]:F3};{lastData["wz"]:F3};" +
            $"{lastData["Roll"]:F3};{lastData["Pitch"]:F3};{lastData["Yaw"]:F3}"
        );
        _csvWriter.Flush();
    }

    private void ReadDataLoop()
    {
        List<byte> buffer = new List<byte>();

        while (_keepReading)
        {
            try
            {
                int newByte = _serialPort.ReadByte();
                if (newByte == -1) continue;
                buffer.Add((byte)newByte);

                if (buffer.Count > 100) buffer.RemoveAt(0);

                int headerIndex = buffer.IndexOf(0x55);
                if (headerIndex != -1 && headerIndex + 1 < buffer.Count && buffer[headerIndex + 1] == 0x61)
                {
                    if (buffer.Count - headerIndex >= 20)
                    {
                        byte[] data = buffer.GetRange(headerIndex, 20).ToArray();
                        buffer.RemoveRange(0, headerIndex + 20);
                        ProcessSensorData(data);
                    }
                }
            }
            catch (TimeoutException) { }
            catch (Exception ex)
            {
                Debug.LogWarning("Serial read error: " + ex.Message);
            }
        }
    }

    private void ProcessSensorData(byte[] data)
    {
        short ax = (short)((data[3] << 8) | data[2]);
        short ay = (short)((data[5] << 8) | data[4]);
        short az = (short)((data[7] << 8) | data[6]);
        short wx = (short)((data[9] << 8) | data[8]);
        short wy = (short)((data[11] << 8) | data[10]);
        short wz = (short)((data[13] << 8) | data[12]);
        short roll = (short)((data[15] << 8) | data[14]);
        short pitch = (short)((data[17] << 8) | data[16]);
        short yaw = (short)((data[19] << 8) | data[18]);

        var dataDict = new Dictionary<string, double>();
        dataDict["ax"] = (ax / 32768.0) * 16.0 * 9.82f;
        dataDict["ay"] = (ay / 32768.0) * 16.0 * 9.82f;
        dataDict["az"] = (az / 32768.0) * 16.0 * 9.82f;
        dataDict["wx"] = (wx / 32768.0) * 2000.0;
        dataDict["wy"] = (wy / 32768.0) * 2000.0;
        dataDict["wz"] = (wz / 32768.0) * 2000.0;
        dataDict["Roll"] = (roll / 32768.0) * 180.0;
        dataDict["Pitch"] = (pitch / 32768.0) * 180.0;
        dataDict["Yaw"] = (yaw / 32768.0) * 180.0;

        Roll = (float)dataDict["Roll"];
        Pitch = (float)dataDict["Pitch"];
        Yaw = (float)dataDict["Yaw"];

        lock (this)
        {
            lastData = dataDict;
        }
    }

    void OnDestroy()
    {
        _keepReading = false;
        if (_readThread != null && _readThread.IsAlive)
        {
            _readThread.Join();
        }

        if (_serialPort != null && _serialPort.IsOpen)
        {
            _serialPort.Close();
            Debug.Log("Serial port closed.");
        }

        if (_csvWriter != null)
        {
            _csvWriter.Close();
            Debug.Log("CSV file closed.");
        }
    }
}
