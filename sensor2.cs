using UnityEngine;
using System;
using System.IO.Ports;
using System.Collections.Generic;
using System.Threading;
using System.IO;

public class sensor2 : MonoBehaviour
{
    [SerializeField] private string portName = "COM5";                  //Port for sensor USB
    [SerializeField] private int baudRate = 115200;                     //Rate from sensor 
    [SerializeField] private float stableDelay = 0.2f;                  //Delay range for stability check
    [SerializeField] private float changeThreshold = 100f;              //Threshold for 
    [SerializeField] private float throwThreshold = 600f;
    [SerializeField] private int thresholdSamplesNeeded = 3;            //Amount of needed succesive datapoints
    [SerializeField] private int stableSamplesNeeded = 3;               //Same as thersholdSamplesNeeded but for other case
    [SerializeField] private float nearZeroThreshold = 150f;            //Find end of throw
    [SerializeField] private int nearZeroSamplesNeeded = 2;             //Consecutive for end of throw
    
    public float Roll { get; private set; }                             //Rotatation 
    public float Pitch { get; private set; }                            //Left right 
    public float Yaw { get; private set; }                              //Front back

    private float estimatedTimestamp = 0f;                              
    private float timeSinceThrown = 0f;                                 
    private float dynamicCenter = 0f;                                   //Range in rotation for a throw
    private float throwStartTime = -1f;                             
    private float flightEndTime = -1f;                      

    private int stableDeltaCounter = 0;                                 //Delta for center 
    private int nearZeroCounter = 0;
    private bool finalCenterSet = false;    


    private enum ThrowState { IDLE, THROWN, IN_FLIGHT, DONE }           //State machine for a throw or not
    private ThrowState currentThrowState = ThrowState.IDLE;             //Starting value for state machine


    private Vector3 velocity = Vector3.zero;         // integrated velocity
    private Vector3 lastAcceleration = Vector3.zero; // acceleration from the previous frame
    private bool isIntegratingVelocity = false;      // are we currently tracking velocity in-flight?


    // --------------------------------------------------------------------------------
    // RING BUFFER to track recent samples (time + wz) so we can retroactively find
    // the start of the throw when we confirm thresholdSamplesNeeded consecutive frames.
    // --------------------------------------------------------------------------------
    private struct FrameSample
    {
        public float time;
        public float wz;
    }

    private const int RING_BUFFER_CAPACITY = 30;
    private List<FrameSample> ringBuffer = new List<FrameSample>(RING_BUFFER_CAPACITY);

    private SerialPort _serialPort;
    private Thread _readThread;
    private bool _keepReading = false;
    private StreamWriter _csvWriter;
    private Dictionary<string, double> lastData = null;

    void Start()
    {
        Application.targetFrameRate = 30;
        estimatedTimestamp = Time.time;

        // open or create a CSV file.
        string csvPath = Application.dataPath + "/BWT901_log_" +
                  DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

        try
        {
            _csvWriter = new StreamWriter(csvPath, false);
            _csvWriter.WriteLine("Timestamp;ax;ay;az;wx;wy;wz;Roll;Pitch;Yaw;Speed");
            _csvWriter.Flush();
            Debug.Log("CSV file opened at: " + csvPath);
        }
        catch (Exception e)
        {
            Debug.LogError("Could not open CSV file for writing: " + e.Message);
        }

        // Initialize and open the serial port.
        _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One);
        _serialPort.ReadTimeout = 500;
        _serialPort.Handshake = Handshake.None;
        _serialPort.DtrEnable = true;
        _serialPort.RtsEnable = true;

        try
        {
            _serialPort.Open();
            Debug.Log("Serial port opened successfully.");

            // Start a background thread to read the sensor data.
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


        float ax = (float)lastData["ax"]; // m/s^2
        float ay = (float)lastData["ay"];
        float az = (float)lastData["az"];

        float rollDeg = (float)lastData["Roll"];
        float pitchDeg = (float)lastData["Pitch"];
        float yawDeg = (float)lastData["Yaw"];

        Vector3 currentAcceleration = new Vector3(ax, ay, az);

        //Ingeration of acceleration to get velocity
        if (isIntegratingVelocity)
        {
            Quaternion sensorToWorld = Quaternion.Euler(pitchDeg, yawDeg, rollDeg);

            // Local acceleration vector
            Vector3 accelLocal = new Vector3(ax, ay, az);

            // Rotate to world frame
            Vector3 accelWorld = sensorToWorld * accelLocal;
            accelWorld.y -= 9.81f;

            // Integrate
            float dt = Time.deltaTime;
            velocity += accelWorld * dt;
        }

        float speed = velocity.magnitude;

        // Add current sample to ringBuffer
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
                        timeSinceThrown = 0f;        
                        finalCenterSet = false;

                        velocity = Vector3.zero;
                        isIntegratingVelocity = true;   // begin integration


                        int earliestIndex = ringBuffer.Count - consecutiveCount;
                        throwStartTime = ringBuffer[earliestIndex].time;
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
                        finalCenterSet = true;  
                        int framesToAvg = Mathf.Min(5, ringBuffer.Count);
                        float sum = 0f;

                        for (int i = ringBuffer.Count - framesToAvg; i < ringBuffer.Count; i++)
                            sum += ringBuffer[i].wz;
                        dynamicCenter = sum / framesToAvg;
                        Debug.Log($"[THROWN] Final center set to {dynamicCenter:F2} after delay");
                    }

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

                isIntegratingVelocity = false;

                break;
        }

        //Debug.Log($"Velocity=({velocity.x:F2}, {velocity.y:F2}, {velocity.z:F2}), Speed={speed:F2}");
        // Save to CSV
        _csvWriter.WriteLine(
            $"{estimatedTimestamp:F3};" +
            $"{lastData["ax"]:F3};{lastData["ay"]:F3};{lastData["az"]:F3};" +
            $"{lastData["wx"]:F3};{lastData["wy"]:F3};{lastData["wz"]:F3};" +
            $"{lastData["Roll"]:F3};{lastData["Pitch"]:F3};{lastData["Yaw"]:F3};{speed:F2}"
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
        
    private void ProcessSensorData(byte[] data)                             //Setup for the diffrent retrived values of the sensor 
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
