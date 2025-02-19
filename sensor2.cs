using UnityEngine;
using System;
using System.IO.Ports;
using System.Collections.Generic;
using System.Threading;
using System.IO;  
using System.Globalization; 


public class sensor2 : MonoBehaviour


{
    public float Roll { get; private set; }
    public float Pitch { get; private set; }
    public float Yaw { get; private set; }
    private float estimatedTimestamp = 0f;
    private float updateInterval = 1f / 200f;
    [SerializeField] private string portName = "COM5";
    [SerializeField] private int baudRate = 115200;

    private SerialPort _serialPort;
    private Thread _readThread;
    private bool _keepReading = false;
    private StreamWriter _csvWriter;

    // Last received data
    private Dictionary<string, double> lastData = null;
    private int frameCount = 0;

    void Start()
    {
        Application.targetFrameRate = 60;
        // Start timestamp at Unity's current time
        estimatedTimestamp = Time.time; 
        
        // 1) Open or create a CSV file.
        string csvPath = Application.dataPath + "/BWT901_log_" +
                  DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".csv";

        try
        {
            _csvWriter = new StreamWriter(csvPath, false);

            // Write a header row using semicolon as delimiter.
            _csvWriter.WriteLine("Timestamp;ax;ay;az;wx;wy;wz;Roll;Pitch;Yaw");
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
        if (lastData != null)
        {
            estimatedTimestamp += Time.deltaTime; // Add elapsed time

            Debug.Log($"Timestamp={estimatedTimestamp:F3}, ax={lastData["ax"]:F3}, " +
                      $"ay={lastData["ay"]:F3}, az={lastData["az"]:F3}, " +
                      $"Roll={lastData["Roll"]:F3}, Pitch={lastData["Pitch"]:F3}, Yaw={lastData["Yaw"]:F3}");

            _csvWriter.WriteLine(
                $"{estimatedTimestamp:F3};" +  // plain numeric timestamp
                $"{lastData["ax"]:F3};{lastData["ay"]:F3};{lastData["az"]:F3};" +
                $"{lastData["wx"]:F3};{lastData["wy"]:F3};{lastData["wz"]:F3};" +
                $"{lastData["Roll"]:F3};{lastData["Pitch"]:F3};{lastData["Yaw"]:F3}");
                _csvWriter.Flush();
        }
    }
    private void ReadDataLoop()
    {
        List<byte> buffer = new List<byte>(); // Circular buffer for finding headers

        while (_keepReading)
        {
            try
            {
                // Read bytes continuously
                int newByte = _serialPort.ReadByte();
                if (newByte == -1) continue; // Ignore empty reads

                buffer.Add((byte)newByte);

                if (buffer.Count > 100) buffer.RemoveAt(0); // Keep last 100 bytes

                // Look for packet header (0x55) followed by flag (0x61)
                int headerIndex = buffer.IndexOf(0x55);
                if (headerIndex != -1 && headerIndex + 1 < buffer.Count && buffer[headerIndex + 1] == 0x61)
                {
                    // Ensure we have 20 bytes after header
                    if (buffer.Count - headerIndex >= 20)
                    {
                        // Extract the valid 20-byte packet
                        byte[] data = buffer.GetRange(headerIndex, 20).ToArray();

                        // Clear buffer up to extracted data
                        buffer.RemoveRange(0, headerIndex + 20);

                        // Process the data
                        ProcessSensorData(data);
                    }
                }
            }
            catch (TimeoutException)
            {

            }
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

        dataDict["ax"] = (ax / 32768.0) * 16.0;
        dataDict["ay"] = (ay / 32768.0) * 16.0;
        dataDict["az"] = (az / 32768.0) * 16.0;
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
    private byte[] ReadExactly(int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = _serialPort.Read(buffer, offset, count - offset);
            if (read <= 0)
                throw new TimeoutException("No data received from serial port.");
            offset += read;
        }
        return buffer;
    }

    void OnDestroy()
    {
        // Stop reading.
        _keepReading = false;
        if (_readThread != null && _readThread.IsAlive)
        {
            _readThread.Join();
        }

        // Close serial port.
        if (_serialPort != null && _serialPort.IsOpen)
        {
            _serialPort.Close();
            Debug.Log("Serial port closed.");
        }

        // 5) Close the CSV file.
        if (_csvWriter != null)
        {
            _csvWriter.Close();
            Debug.Log("CSV file closed.");
        }
    }
}