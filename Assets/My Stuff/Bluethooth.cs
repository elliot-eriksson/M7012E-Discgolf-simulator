using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using TMPro;

#if UNITY_WSA && !UNITY_EDITOR
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
#endif


public class SensorBluetooth : MonoBehaviour
{
    public delegate void ThrowDetectedEvent(ThrowData throwData);
    public static event ThrowDetectedEvent OnThrowDetected;

    private bool isDetectingThrow = false; // Flag to control if we are detecting a throw

    public TextMeshProUGUI connectionStatusText;
    public TextMeshProUGUI tempText;

    [SerializeField] private float stableDelay = 0.2f;                  //Delay range for stability check
    [SerializeField] private float changeThreshold = 100f;              //Threshold for 
    [SerializeField] private float throwThreshold = 600f;
    [SerializeField] private int thresholdSamplesNeeded = 3;            //Amount of needed succesive datapoints
    [SerializeField] private int stableSamplesNeeded = 3;               //Same as thersholdSamplesNeeded but for other case
    [SerializeField] private float nearZeroThreshold = 150f;            //Find end of throw
    [SerializeField] private int nearZeroSamplesNeeded = 2;             //Consecutive for end of throw

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
    private bool isIntegratingVelocity = false;

    private const int RING_BUFFER_CAPACITY = 30;

    private List<FrameSample> ringBuffer = new List<FrameSample>(RING_BUFFER_CAPACITY);

    private Dictionary<string, double> lastData = null;
    private Dictionary<string, double> actualData = null;

    private Dictionary<string, double> firstData = null;
    private Dictionary<string, double> secondData = null;

    private Dictionary<string, double> maxData = null;

    private float lastTime = 0f;
    private float throwTime = 0f;


    private struct FrameSample
    {
        public float time;
        public float wz;
    }

    public void StartNewThrowDetection()
    {
        // Start the throw detection process
        isDetectingThrow = true;

        lastData = null;
        actualData = null;

        estimatedTimestamp = 0f;
        timeSinceThrown = 0f;
        dynamicCenter = 0f;                                   //Range in rotation for a throw
        throwStartTime = -1f;
        flightEndTime = -1f;
        velocity = Vector3.zero;
        currentThrowState = ThrowState.IDLE;
        ringBuffer.Clear();
        lastAcceleration = Vector3.zero;
        isIntegratingVelocity = false;
        stableDeltaCounter = 0;
        nearZeroCounter = 0;

        firstData = null;
        secondData = null;
        lastTime = 0f;
        throwTime = 0f;

        maxData = null;

    }


#if UNITY_WSA && !UNITY_EDITOR
    private BluetoothLEDevice device;
    private GattCharacteristic characteristic;
    //private ulong sensorAddress = 0xF3_54_9C_0C_0C_F5; // Fria sensorn
    private ulong sensorAddress = 0xC9_65_16_F6_7A_25; //Disc sensorn




    void Start()
    {
        Application.targetFrameRate = 30;
        estimatedTimestamp = Time.time; // Start timestamp at Unity's current time
        UpdateConnectionStatus("Connecting...");
        isDetectingThrow = true;
        ConnectToDevice(sensorAddress);

    }

    void Update()
    {
        if (lastData != null && isDetectingThrow)
        {
            
            
            estimatedTimestamp += Time.deltaTime;
            float currentWz = (float)lastData["wz"];

            float ax = (float)lastData["ax"]; // m/s^2
            float ay = (float)lastData["ay"];
            float az = (float)lastData["az"];

            float rollDeg = (float)lastData["roll"];
            float pitchDeg = (float)lastData["pitch"];
            float yawDeg = (float)lastData["yaw"];

            // Integration of acceleration to get velocity
            if (isIntegratingVelocity && secondData != null && actualData == null)
            {
                float Fax = (float)firstData["ax"];
                float Fay = (float)firstData["ay"];
                float Faz = (float)firstData["az"];
                Vector3 firstAccel = new Vector3(Fax, Fay, Faz);

                //float Sax = (float)secondData["ax"];
                //float Say = (float)secondData["ay"];
                //float Saz = (float)secondData["az"];

                //float dt = throwTime - lastTime;
                float dt = 0.2f;

                Quaternion firstToWorld = Quaternion.Euler((float)firstData["roll"], (float)firstData["pitch"], (float)firstData["yaw"]);
                //Quaternion secondToWorld = Quaternion.Euler((float)secondData["roll"], (float)secondData["pitch"], (float)secondData["yaw"]);
                Quaternion largeToWorld = Quaternion.Euler((float)maxData["roll"], (float)maxData["pitch"], (float)maxData["yaw"]);
                rollDeg = (float)maxData["roll"];
                pitchDeg = (float)maxData["pitch"];
                yawDeg = (float)maxData["yaw"];

                Vector3 firstWorld= firstToWorld * new Vector3(Fax, Fay, Faz);
                //Vector3 secondAccel = secondToWorld * new Vector3(Sax, Say, Saz);
                Vector3 tempAcc = new Vector3((float)maxData["ax"], (float)maxData["ay"], (float)maxData["az"]);
                Vector3 largeAccel = largeToWorld * tempAcc;
                //firstAccel.z = 0f;
                //secondAccel.z = 0f;
                //largeAccel.z = 0f;

                //velocity = (firstAccel + secondAccel) * dt / 2f;
                //velocity = firstAccel * dt;
                //velocity = new Vector3(30, 8, 10) * dt;
                velocity = largeAccel * dt;
                velocity = new Vector3(velocity.z, velocity.y, velocity.x);
                TempTextStatus($"Acceleration {tempAcc},{firstAccel}, \n WorldAcc {largeAccel},{firstWorld}, \n Velocity {velocity}");


                //// Convert Euler angles to quaternion (Verify order based on WT9011DCL docs)
                //Quaternion sensorToWorld = Quaternion.Euler(rollDeg, pitchDeg, yawDeg); 

                //// Rotate acceleration into the world frame
                //Vector3 accelLocal = new Vector3(ay, ax, az);
                //Vector3 accelWorld = sensorToWorld * accelLocal;
                //accelWorld.z = 0f;

                //// Subtract gravity (assuming Y is up in world frame)
                ////Vector3 newAccelWorld -= new Vector3(0, 9.81f, 0);

                //// Integrate acceleration to get velocity
                //float dt = Time.deltaTime;
                //Vector3 currAccel = accelWorld * dt;
                //if (currAccel.magnitude < 0.05f) currAccel = Vector3.zero;
                //velocity += currAccel;

                // Apply simple drift correction (Zero-Velocity Update - optional)
                //if (velocity.magnitude < 0.1f) velocity = Vector3.zero;
                //TempTextStatus($"Local {accelLocal}, {, \n world {accelWorld}, \n Velocity {velocity}");

                // Store velocity in the lastData dictionary

                lastData["pitch"] = pitchDeg;
                lastData["roll"] = rollDeg;
                lastData["yaw"] = yawDeg;

                lastData["vx"] = velocity.x;
                lastData["vy"] = velocity.y; 
                lastData["vz"] = velocity.z;
                actualData = lastData;
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
                        firstData = null;
                        secondData = null;
                        // Count how many consecutive frames from the end are above throwThreshold
                        int consecutiveCount = 0;
                        for (int i = ringBuffer.Count - 1; i >= 0; i--)
                        {
                            if (Mathf.Abs(ringBuffer[i].wz) >= throwThreshold)
                            {
                                consecutiveCount++;
                                if (maxData == null)
                                {
                                    maxData = lastData;
                                }
                                else if (Mathf.Abs((float)maxData["ax"]) < Mathf.Abs((float)lastData["ax"]))
                                {
                                    maxData = lastData;
                                }
                            }
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
                            throwTime = estimatedTimestamp;
                            firstData = lastData;
                            if (Mathf.Abs((float)maxData["ax"]) < Mathf.Abs((float)lastData["ax"]))
                            {
                                maxData = lastData;
                            }

                        }
                    }
                    break;

                case ThrowState.THROWN:
                    {
                        //actualData = lastData;
                        timeSinceThrown += Time.deltaTime;
                        if (secondData == null)
                        {
                            lastTime = timeSinceThrown;
                            secondData = lastData;
                        }
                        if (Mathf.Abs((float)maxData["ax"]) < Mathf.Abs((float)lastData["ax"]))
                        {
                            maxData = lastData;
                        }

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
                        if (Mathf.Abs((float)maxData["ax"]) < Mathf.Abs((float)lastData["ax"]))
                        {
                            maxData = lastData;
                        }

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

                        ThrowData throwData = new ThrowData(estimatedTimestamp, timeSinceThrown, actualData);
                        isDetectingThrow = false;
                        isIntegratingVelocity = false;
                        //TempTextStatus($"Timestamp={estimatedTimestamp:F3},\n ax={actualThrow["ax"]:F3},\n " +
                        //  $"ay={actualThrow["ay"]:F3},\n az={actualThrow["az"]:F3}, \n" +
                        //  $"Roll={actualThrow["roll"]:F3},\n Pitch={actualThrow["pitch"]:F3},\n Yaw={actualThrow["yaw"]:F3}\n" +
                        //    $"wx={actualThrow["wx"]:F3};\n wy= {actualThrow["wy"]:F3};\n wz= {actualThrow["wz"]:F3},\n Throw Time={throwTimestamp:F3}");
                        //OnThrowDetected(throwData);
                        OnThrowDetected?.Invoke(throwData);
                        break;
                }
            } 

            UpdateConnectionStatus($"Timestamp={estimatedTimestamp:F3},\n ax={lastData["ax"]:F3},\n " +
            $"ay={lastData["ay"]:F3},\n az={lastData["az"]:F3}, \n" +
            $"Roll={lastData["roll"]:F3},\n Pitch={lastData["pitch"]:F3},\n Yaw={lastData["yaw"]:F3}\n" +
            $"wx={lastData["wx"]:F3};\n wy= {lastData["wy"]:F3};\n wz= {lastData["wz"]:F3},\n ");

    }


    private async void ConnectToDevice(ulong address)
    {
        try
        {
            device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
            if (device == null)
            {
                UpdateConnectionStatus("Device not found. Check if it's turned on.");
                return;
            }

            UpdateConnectionStatus("Connected to: " + device.Name);
            Debug.Log("Connected to: " + device.Name);

            var servicesResult = await device.GetGattServicesAsync();
            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                UpdateConnectionStatus("Failed to retrieve services. Check Bluetooth permissions.");
                return;
            }

            foreach (var service in servicesResult.Services)
            {
                var characteristicsResult = await service.GetCharacteristicsAsync();
                foreach (var charac in characteristicsResult.Characteristics)
                {
                    if (charac.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                    {
                        characteristic = charac;
                        await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.Notify);
                        characteristic.ValueChanged += Characteristic_ValueChanged;
                        UpdateConnectionStatus("Receiving sensor data...");
                        Debug.Log("Subscribed to sensor data!");
                        return;
                    }
                }
            }

            UpdateConnectionStatus("No valid characteristics found.");
        }
        catch (Exception ex)
        {
            UpdateConnectionStatus($"Connection error: {ex.Message}");
        }
    }

    private void Characteristic_ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        byte[] data;
        using (var reader = DataReader.FromBuffer(args.CharacteristicValue))
        {
            data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);
        }

        ProcessSensorData(data);
    }

    private void ProcessSensorData(byte[] data)
    {
        if (data.Length < 20) return; // Ensure enough bytes are received

        short ax = (short)((data[3] << 8) | data[2]);
        short ay = (short)((data[5] << 8) | data[4]);
        short az = (short)((data[7] << 8) | data[6]);
        short wx = (short)((data[9] << 8) | data[8]);
        short wy = (short)((data[11] << 8) | data[10]);
        short wz = (short)((data[13] << 8) | data[12]);
        short roll = (short)((data[15] << 8) | data[14]);
        short pitch = (short)((data[17] << 8) | data[16]);
        short yaw = (short)((data[19] << 8) | data[18]);

        var dataDict = new Dictionary<string, double>
        {
            ["ax"] = (ax / 32768.0) * 16.0 * 9.82f,
            ["ay"] = (ay / 32768.0) * 16.0 * 9.82f,
            ["az"] = (az / 32768.0) * 16.0 * 9.82f,

            ["wx"] = (wx / 32768.0) * 2000.0,
            ["wy"] = (wy / 32768.0) * 2000.0,
            ["wz"] = (wz / 32768.0) * 2000.0,

            ["roll"] = (roll / 32768.0) * 180.0,
            ["pitch"] = (pitch / 32768.0) * 180.0,
            ["yaw"] = (yaw / 32768.0) * 180.0
        };


        lock (this)
        {
            lastData = dataDict;
        }
    }


    private void UpdateConnectionStatus(string status)
    {
        if (connectionStatusText != null)
        {
            connectionStatusText.text = status;
        }
    }

    private void TempTextStatus(string status)
    {
        if (tempText != null)
        {
            tempText.text = status;
        }
    }

    void OnDestroy()
    {
        //Debug.Log($"Throw detected at {throwTimestamp:F3} seconds");

        if (device != null)
        {
            device.Dispose();
            Debug.Log("Bluetooth device connection closed.");
        }
    }
#endif
}
