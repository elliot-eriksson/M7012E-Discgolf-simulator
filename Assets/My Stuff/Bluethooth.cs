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
    public TextMeshProUGUI connectionStatusText; // UI element for displaying connection status

#if UNITY_WSA && !UNITY_EDITOR
    private BluetoothLEDevice device;
    private GattCharacteristic characteristic;
    //private ulong sensorAddress = 0xF3_54_9C_0C_0C_F5; // Fria sensorn
    private ulong sensorAddress = 0xC9_65_16_F6_7A_25; //Disc sensorn

    private Dictionary<string, double> lastData = null;
    public float Roll { get; private set; }
    public float Pitch { get; private set; }
    public float Yaw { get; private set; }

    private Thread _readThread;
    private float estimatedTimestamp = 0f;

    void Start()
    {
        estimatedTimestamp = Time.time; // Start timestamp at Unity's current time
        UpdateConnectionStatus("Connecting...");
        ConnectToDevice(sensorAddress);
    }

    void Update()
    {
        if (lastData != null)
        {
            estimatedTimestamp += Time.deltaTime; // Add elapsed time

            Debug.Log($"Timestamp={estimatedTimestamp:F3} ,\n ax={lastData["ax"]:F3}, \n" +
                      $"ay={lastData["ay"]:F3},\n az={lastData["az"]:F3},\n " +
                      $"Roll={lastData["Roll"]:F3}, Pitch={lastData["Pitch"]:F3}, Yaw={lastData["Yaw"]:F3}" +
                    $"wx={lastData["wx"]:F3}; wy= {lastData["wy"]:F3}; wz= {lastData["wz"]:F3};");

            UpdateConnectionStatus($"Timestamp={estimatedTimestamp:F3},\n ax={lastData["ax"]:F3},\n " +
                      $"ay={lastData["ay"]:F3},\n az={lastData["az"]:F3}, \n" +
                      $"Roll={lastData["Roll"]:F3},\n Pitch={lastData["Pitch"]:F3},\n Yaw={lastData["Yaw"]:F3}\n" +
                        $"wx={lastData["wx"]:F3};\n wy= {lastData["wy"]:F3};\n wz= {lastData["wz"]:F3};");

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
            ["Roll"] = (roll / 32768.0) * 180.0,
            ["Pitch"] = (pitch / 32768.0) * 180.0,
            ["Yaw"] = (yaw / 32768.0) * 180.0
        };

        Roll = (float)dataDict["Roll"];
        Pitch = (float)dataDict["Pitch"];
        Yaw = (float)dataDict["Yaw"];

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

    void OnDestroy()
    {
        if (device != null)
        {
            device.Dispose();
            Debug.Log("Bluetooth device connection closed.");
        }
    }
#endif
}
