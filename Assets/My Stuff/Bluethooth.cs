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
    //public TextMeshProUGUI tempText; 


    private Dictionary<string, double> lastData = null;
    private Dictionary<string, double> actualThrow = null;

    private Thread _readThread;
    private float estimatedTimestamp = 0f;
    private float updateInterval = 1f / 200f;

    private float throwTimestamp = 0f;
    private float maxWz = 0f;
    private float throwDetectedTime = -1f;
    private float endThrow = 0f;

    private Vector3 velocity = Vector3.zero;

    public void StartNewThrowDetection()
    {
        // Start the throw detection process
        isDetectingThrow = true;

        lastData = null;
        actualThrow = null;

        // Reset throw-related variables
        throwTimestamp = 0f;
        maxWz = 0f;
        throwDetectedTime = -1f;
        endThrow = 0f;
        velocity = Vector3.zero;
    }


    // Step 1: Throw detection states
    private enum ThrowState { IDLE, THROWN, IN_FLIGHT, DONE }
    private ThrowState currentThrowState = ThrowState.IDLE;

    [SerializeField] private float throwThreshold = 600f;       // For crossing from IDLE -> THROWN
    [SerializeField] private int thresholdSamplesNeeded = 6;   // # of consecutive frames above throwThreshold

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

#if UNITY_WSA && !UNITY_EDITOR
    private BluetoothLEDevice device;
    private GattCharacteristic characteristic;
    //private ulong sensorAddress = 0xF3_54_9C_0C_0C_F5; // Fria sensorn
    private ulong sensorAddress = 0xC9_65_16_F6_7A_25; //Disc sensorn


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

        if (isDetectingThrow && lastData != null)
        {

            estimatedTimestamp += Time.deltaTime; // Track time

            float currentWz = (float)lastData["wz"]; // Current Z-axis rotation speed

            // Check if this is the highest rotation speed we've seen
            if (Mathf.Abs(currentWz) > Mathf.Abs(maxWz))
            {
                maxWz = currentWz;
                throwTimestamp = estimatedTimestamp; // Store when max rotation occurs
                actualThrow = lastData;
            }



            FrameSample sample = new FrameSample { time = estimatedTimestamp, wz = currentWz };
            ringBuffer.Add(sample);
            if (ringBuffer.Count > RING_BUFFER_CAPACITY)
            {
                ringBuffer.RemoveAt(0);
            }


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
                    //TempTextStatus("THROWN");
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

                    }
                    break;

                case ThrowState.IN_FLIGHT:
                    Debug.Log("IN FLIGHT");
                    //TempTextStatus("IN FLIGHT");
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

                    ThrowData throwData = new ThrowData(throwTimestamp, estimatedTimestamp - throwTimestamp, actualThrow);
                    isDetectingThrow = false;
                    //TempTextStatus($"Timestamp={estimatedTimestamp:F3},\n ax={actualThrow["ax"]:F3},\n " +
                    //  $"ay={actualThrow["ay"]:F3},\n az={actualThrow["az"]:F3}, \n" +
                    //  $"Roll={actualThrow["roll"]:F3},\n Pitch={actualThrow["pitch"]:F3},\n Yaw={actualThrow["yaw"]:F3}\n" +
                    //    $"wx={actualThrow["wx"]:F3};\n wy= {actualThrow["wy"]:F3};\n wz= {actualThrow["wz"]:F3},\n Throw Time={throwTimestamp:F3}");
                    OnThrowDetected(throwData);
                    break;
            }



            Debug.Log($"Timestamp={estimatedTimestamp:F3} ,\n ax={lastData["ax"]:F3}, \n" +
                      $"ay={lastData["ay"]:F3},\n az={lastData["az"]:F3},\n " +
                      $"Roll={lastData["roll"]:F3}, Pitch={lastData["pitch"]:F3}, Yaw={lastData["yaw"]:F3}" +
                    $"wx={lastData["wx"]:F3}; wy= {lastData["wy"]:F3}; wz= {lastData["wz"]:F3};");

            UpdateConnectionStatus($"Timestamp={estimatedTimestamp:F3},\n ax={lastData["ax"]:F3},\n " +
                      $"ay={lastData["ay"]:F3},\n az={lastData["az"]:F3}, \n" +
                      $"Roll={lastData["roll"]:F3},\n Pitch={lastData["pitch"]:F3},\n Yaw={lastData["yaw"]:F3}\n" +
                        $"wx={lastData["wx"]:F3};\n wy= {lastData["wy"]:F3};\n wz= {lastData["wz"]:F3},\n Throw Time={throwTimestamp:F3}");

        }             
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

    //private void TempTextStatus(string status)
    //{
    //    if (tempText != null)
    //    {
    //        tempText.text = status;
    //    }
    //}

    void OnDestroy()
    {
        Debug.Log($"Throw detected at {throwTimestamp:F3} seconds");

        if (device != null)
        {
            device.Dispose();
            Debug.Log("Bluetooth device connection closed.");
        }
    }
#endif
}
