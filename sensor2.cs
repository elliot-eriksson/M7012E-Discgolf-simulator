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

    // Step 1: Throw detection states
    private enum ThrowState { IDLE, THROWN, IN_FLIGHT, DONE }
    private ThrowState currentThrowState = ThrowState.IDLE;

    [SerializeField] private float throwThreshold = 600f;       // For crossing from IDLE -> THROWN
    [SerializeField] private int thresholdSamplesNeeded = 6;   // # of consecutive frames above throwThreshold
    //private int thresholdCounter = 0;

    // We'll capture the rotation at the moment of throw
    private float dynamicCenter = 0f;

    // Used once we're in THROWN
    [SerializeField] private float stableBand = 100f;
    [SerializeField] private float stableRotationCenter = -1400f;
    [SerializeField] private float stableRotationBand = 60f;
    [SerializeField] private int stableSamplesNeeded = 6;
    private int stableCounter = 0;

    // Used once we're in IN_FLIGHT
    [SerializeField] private float nearZeroThreshold = 100f;
    [SerializeField] private int nearZeroSamplesNeeded = 5;
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

        // Track the max rotation for reference
        if (Mathf.Abs(currentWz) > Mathf.Abs(maxWz))
        {
            maxWz = currentWz;
            throwTimestamp = estimatedTimestamp;
        }

        // --------------------------------------------------------------------------------
        //  Push the current frame into our ring buffer (time + wz). 
        //  If we exceed capacity, remove the oldest sample.
        // --------------------------------------------------------------------------------
        FrameSample sample = new FrameSample { time = estimatedTimestamp, wz = currentWz };
        ringBuffer.Add(sample);
        if (ringBuffer.Count > RING_BUFFER_CAPACITY)
        {
            ringBuffer.RemoveAt(0);
        }

        // --------------------------------------------------------------------------------
        //  State Machine
        // --------------------------------------------------------------------------------
        switch (currentThrowState)
        {
            case ThrowState.IDLE:
                {
                    // We look at the end of the ringBuffer to see how many consecutive samples 
                    // are above the throwThreshold, going backwards from the newest sample.
                    int consecutiveCount = 0;
                    for (int i = ringBuffer.Count - 1; i >= 0; i--)
                    {
                        float wzVal = ringBuffer[i].wz;
                        if (Mathf.Abs(wzVal) >= throwThreshold)
                        {
                            consecutiveCount++;
                        }
                        else
                        {
                            // As soon as we find one sample below threshold, stop counting.
                            break;
                        }
                    }

                    // If we found enough consecutive frames at the end of the buffer:
                    if (consecutiveCount >= thresholdSamplesNeeded)
                    {
                        // 1) Transition to THROWN
                        currentThrowState = ThrowState.THROWN;

                        // 2) The earliest sample of that run is:
                        int earliestIndex = ringBuffer.Count - consecutiveCount;
                        float realStartTime = ringBuffer[earliestIndex].time;
                        throwStartTime = realStartTime;

                        // 3) Pick a dynamicCenter. 
                        // For example, use the last sample's rotation (the newest sample).
                        // Or compute an average of those consecutive frames for a smoother center.
                        float lastWz = ringBuffer[ringBuffer.Count - 1].wz;
                        dynamicCenter = lastWz;

                        Debug.Log($"[STATE] Throw detected! Real start time = {throwStartTime:F3}, dynamicCenter={dynamicCenter:F2}");
                    }
                }
                break;


            case ThrowState.THROWN:
                //Debug.Log("THROWN");
                // If current rotation is close to dynamicCenter, increment stableCounter
                if (currentWz > dynamicCenter - stableBand &&
                    currentWz < dynamicCenter + stableBand)
                {
                    stableCounter++;
                    Debug.Log("Counter="+stableCounter);
                    if (stableCounter >= stableSamplesNeeded)
                    {
                        currentThrowState = ThrowState.IN_FLIGHT;
                        Debug.Log($"[STATE] Stable flight at {estimatedTimestamp:F3} (center={dynamicCenter:F2})");
                    }
                }
                else
                {
                    // Not in the band => reset stableCounter
                    stableCounter = 0;

                    Debug.Log(Mathf.Abs(currentWz));
                    // If rotation
                    // dips below the throw threshold again, it's probably a false start
                    //if (Mathf.Abs(currentWz) < throwThreshold)
                    //{
                    //    currentThrowState = ThrowState.IDLE;
                    //    thresholdCounter = 0;
                    //    Debug.Log("[STATE] Throw aborted. Back to IDLE.");
                    //}
                }
                break;

            case ThrowState.IN_FLIGHT:
                Debug.Log("IN FLIGHT");
                // If rotation dips below nearZeroThreshold for enough frames, we're done
                if (Mathf.Abs(currentWz) < nearZeroThreshold)
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
