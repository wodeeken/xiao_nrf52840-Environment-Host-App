using System;
using Tmds.DBus;
using bluez.DBus;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
namespace xiao_nrf52840_Environment_Host_App
{

    class Program
    {
        // Main entry point. 
        static void Main(string[] args)
        {
            double maxFileSize;
            if (args.Count() < 7)
            {
                throw new ArgumentException("No arguments passed. Expecting 7 in the following order: " +
                "{Image data directory, audio data directory, air temp directory, air pressure directory, air humidity directory, enclosure temp directory, max air monitor data file size in Kb (triggers archiving)}.");
            }
            else
            {
                for (int i = 0; i < args.Count() - 1; i++)
                {
                    if (!Directory.Exists(args[i]))
                    {
                        throw new ArgumentException($"Arg path at index {i} ({args[i]}) does not point to a valid directory.");
                    }
                }
                if (!double.TryParse(args[args.Count() - 1], out maxFileSize) || maxFileSize < 1)
                {
                    throw new ArgumentException($"Arg Max file size at index {args.Count() - 1} ({args[args.Count() - 1]}) is not in proper format (cannot be less than 1 kb )");
                }
            }
            // All main code is in MainAsync.
            MainAsync(args[0], args[1], args[2], args[3], args[4], args[5], maxFileSize).Wait();
        }

        static async Task<List<IGattCharacteristic1>> ConnectToEnvironmentalMonitor()
        {
            IDevice1 myDevice = await BluetoothUtilities.ScanAndConnect(Constants.DeviceToConnect, Constants.DeviceToConnect_ConnectedName);
            // After connecting, find all relevant characteristics (Camera Trigger/Data, Temperature, and Pressure);
            List<IGattCharacteristic1> characteristics = await BluetoothUtilities.GetCharacteristics(new List<string>(){Constants.BLECameraCharacteristic,
            Constants.BLEAudioCharacteristic,
            Constants.BLEAudioSampleRateCharacteristic,
            Constants.BLEAudioSampleRateCharacteristic,
            Constants.BLEEnclosureTemperatureCharacteristic,
            Constants.BLEAirPressureCharacteristic,
            Constants.BLEHumidityCharacteristic,
            Constants.BLETemperatureCharacteristic});
            return characteristics;

        }
        static async Task MainAsync(string cameraDataPath, string audioDataPath, string airTempDataPath, string airPressureDataPath, string airHumidityDataPath, string enclosureAirTempDataPath, double maxFileSize)
        {
            while (true)
            {
                try
                {
                    List<IGattCharacteristic1> characteristics = await ConnectToEnvironmentalMonitor();
                    // Get camera characteristic.
                    IGattCharacteristic1 audioCharacteristic = null;
                    IGattCharacteristic1 audioSampleRateCharacteristic = null;
                    IGattCharacteristic1 cameraCharacteristic = null;
                    IGattCharacteristic1 enclosureTemperatureCharacteristic = null;
                    IGattCharacteristic1 airPressureCharacteristic = null;
                    IGattCharacteristic1 humidityCharacteristic = null;
                    IGattCharacteristic1 temperatureCharacteristic = null;
                    foreach (IGattCharacteristic1 characteristic in characteristics)
                    {
                        string UUID = await characteristic.GetUUIDAsync();
                        if (UUID.ToLower() == Constants.BLECameraCharacteristic)
                        {
                            cameraCharacteristic = characteristic;
                        }
                        else if (UUID.ToLower() == Constants.BLEEnclosureTemperatureCharacteristic)
                        {
                            enclosureTemperatureCharacteristic = characteristic;
                        }
                        else if (UUID.ToLower() == Constants.BLEAirPressureCharacteristic)
                        {
                            airPressureCharacteristic = characteristic;
                        }
                        else if (UUID.ToLower() == Constants.BLEHumidityCharacteristic)
                        {
                            humidityCharacteristic = characteristic;
                        }
                        else if (UUID.ToLower() == Constants.BLETemperatureCharacteristic)
                        {
                            temperatureCharacteristic = characteristic;
                        }
                        else if (UUID.ToLower() == Constants.BLEAudioCharacteristic)
                        {
                            audioCharacteristic = characteristic;
                        }
                        else if (UUID.ToLower() == Constants.BLEAudioSampleRateCharacteristic)
                        {
                            audioSampleRateCharacteristic = characteristic;
                        }
                    }
                    // Stay in a loop.
                    while (true)
                    {
                        RotateDataFiles(airTempDataPath, airPressureDataPath, airHumidityDataPath, enclosureAirTempDataPath, maxFileSize);
                        //await TakeCameraImage(cameraCharacteristic, cameraDataPath);
                        await ReadEnclosureTemperature(enclosureTemperatureCharacteristic, enclosureAirTempDataPath);
                        await ReadPressure(airPressureCharacteristic, airPressureDataPath);
                        await ReadHumidity(humidityCharacteristic, airHumidityDataPath);
                        await ReadTemperature(temperatureCharacteristic, airTempDataPath);
                        //await RecordMicrophoneAudio(audioCharacteristic, audioSampleRateCharacteristic, audioDataPath);
                    }
                }
                catch (Exception e)
                {
                    Console.Write(e);
                }

            }
        }

        static async Task TakeCameraImage(IGattCharacteristic1 cameraCharacteristic, string cameraDataPath)
        {
            await cameraCharacteristic.WriteValueAsync(Constants.BLECameraCharacteristicValue_Trigger, new Dictionary<string, object>());
            Console.WriteLine("Triggering Camera!");
            // Wait a few seconds for camera to work.
            System.Threading.Thread.Sleep(5000);
            byte[] characteristicValue;
            characteristicValue = await cameraCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int totalPackets = characteristicValue[0] << 8 | characteristicValue[1];
            byte[] CameraData = new byte[totalPackets * 244];
            for (int currentPacket = 0; currentPacket < totalPackets; currentPacket++)
            {
                Console.Write(currentPacket); Console.WriteLine($" of {totalPackets}");
                await cameraCharacteristic.WriteValueAsync(GetPacketFetchMessage(currentPacket), new Dictionary<string, object>());
                characteristicValue = await cameraCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                // Stay in loop until we read a valid value.
                while (characteristicValue.Count() <= 1)
                {
                    System.Threading.Thread.Sleep(10);
                    characteristicValue = await cameraCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                }
                // Get the data, save to cam data array.
                int currentValIndex = 0;
                for (int i = currentPacket * 244; i < (currentPacket * 244) + characteristicValue.Count(); i++)
                {
                    CameraData[i] = characteristicValue[currentValIndex];
                    currentValIndex++;
                }
            }
            File.WriteAllBytes(Path.Combine(cameraDataPath, DateTime.Now.ToString("yyyy_MM_dd HH:mm:ss") + ".jpg"), CameraData);
        }
        static async Task RecordMicrophoneAudio(IGattCharacteristic1 audioCharacteristic, IGattCharacteristic1 audioSampleRateCharacteristic, string audioDataPath)
        {

            await audioCharacteristic.WriteValueAsync(Constants.BLEAudioCharacteristicValue_Trigger, new Dictionary<string, object>());


            Console.WriteLine("Triggering Microphone!");
            // Wait 20 seconds for audio recording to finish.
            System.Threading.Thread.Sleep(20000);
            byte[] characteristicValue;
            characteristicValue = await audioCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int totalPackets = characteristicValue[0] << 8 | characteristicValue[1];
            byte[] AudioData = new byte[totalPackets * 244];
            for (int currentPacket = 0; currentPacket < totalPackets; currentPacket++)
            {
                Console.Write(currentPacket); Console.WriteLine($" of {totalPackets}");
                await audioCharacteristic.WriteValueAsync(GetPacketFetchMessage(currentPacket), new Dictionary<string, object>());
                characteristicValue = await audioCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                // Stay in loop until we read a valid value.
                while (characteristicValue.Count() <= 1)
                {
                    System.Threading.Thread.Sleep(10);
                    characteristicValue = await audioCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                }
                // Get the data, save to data array.
                int currentValIndex = 0;
                for (int i = currentPacket * 244; i < (currentPacket * 244) + characteristicValue.Count(); i++)
                {
                    AudioData[i] = characteristicValue[currentValIndex];
                    currentValIndex++;
                }
            }
            // Write raw audio.
            string rawAudioPath = Path.Combine(audioDataPath, DateTime.Now.ToString("yyyy_MM_dd HH:mm:ss"));
            
            File.WriteAllBytes($"{rawAudioPath}.raw", AudioData);
            // Wait one second before reading the sample rate characteristic/
            System.Threading.Thread.Sleep(1000);
            int sampleRate = await ReadAudioSampleRate(audioSampleRateCharacteristic);
            // Perform Audio conversion to wav.
            string conversionCommand = $"sox -r {sampleRate} -e signed -b 16 -B  '{rawAudioPath}.raw' '{rawAudioPath}.wav'";
            CommandHelper(conversionCommand);
            // delete raw audio.
            File.Delete($"{rawAudioPath}.raw");

        }
        // Find 0xA1 and 0xA2 and replace with high and low byte of packetToFetch integer.
        static byte[] GetPacketFetchMessage(int packetToFetch)
        {
            byte[] returnMessage = new byte[Constants.BLECamera_AudioCameraCharacteristicValue_PacketFetch.Length];
            Constants.BLECamera_AudioCameraCharacteristicValue_PacketFetch.CopyTo(returnMessage, 0);
            ushort curPacket_short = Convert.ToUInt16(packetToFetch);
            for (int i = 0; i < returnMessage.Length; i++)
            {
                if (returnMessage[i].CompareTo(0xA1) == 0)
                {
                    returnMessage[i] = (byte)(curPacket_short >> 8);
                }
                else if (returnMessage[i].CompareTo(0xA2) == 0)
                {
                    returnMessage[i] = (byte)(curPacket_short & 0xff);
                }

            }
            return returnMessage;
        }
        static async Task ReadEnclosureTemperature(IGattCharacteristic1 tempCharacteristic, string enclosureTempDataPath)
        {
            byte[] characteristicValue;
            characteristicValue = await tempCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            // Temp is rounded to nearest integer.
            int temp = characteristicValue[0] << 8 | characteristicValue[1];
            Console.Write("Temp Reading: ");
            Console.Write(temp);
            Console.WriteLine(" °C");
            string dataString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " - " + temp.ToString() + "°C" + Environment.NewLine;
            File.AppendAllText(Path.Combine(enclosureTempDataPath, Constants.EnclosureAirTemperatureDataFileName), dataString);
        }
        static async Task ReadPressure(IGattCharacteristic1 pressureCharacteristic, string pressureDataPath)
        {
            byte[] characteristicValue;
            characteristicValue = await pressureCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int pressure = characteristicValue[0] << 8 | characteristicValue[1];
            Console.Write("Pressure Reading: ");
            Console.Write(pressure);
            Console.WriteLine(" hPa");
            string dataString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " - " + pressure.ToString() + " hPa" + Environment.NewLine;
            File.AppendAllText(Path.Combine(pressureDataPath, Constants.AirPressureDataFileName), dataString);
        }
        static async Task ReadHumidity(IGattCharacteristic1 humidityCharacteristic, string humidityDataPath)
        {
            byte[] characteristicValue;
            characteristicValue = await humidityCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int humidity = characteristicValue[0] << 8 | characteristicValue[1];
            Console.Write("Humidity Reading: ");
            Console.Write(humidity);
            Console.WriteLine("%");
            string dataString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " - " + humidity.ToString() + "%" + Environment.NewLine;
            File.AppendAllText(Path.Combine(humidityDataPath, Constants.HumidityDataFileName), dataString);
        }
        static async Task ReadTemperature(IGattCharacteristic1 tempCharacteristic, string temperatureDataPath)
        {
            byte[] characteristicValue;
            characteristicValue = await tempCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int temp = characteristicValue[0] << 8 | characteristicValue[1];
            Console.Write("Temp: ");
            Console.WriteLine(temp);
            Console.Write(" °C");
            string dataString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " - " + temp.ToString() + "°C" + Environment.NewLine;
            File.AppendAllText(Path.Combine(temperatureDataPath, Constants.OutsideAirTemperatureDataFileName), dataString);
        }
        static async Task<int> ReadAudioSampleRate(IGattCharacteristic1 audioSampleRateCharacteristic)
        {
            byte[] characteristicValue;
            characteristicValue = await audioSampleRateCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int audioSampleRate = characteristicValue[0] << 8 | characteristicValue[1];
            Console.Write("Audio Sample Rate: ");
            Console.WriteLine(audioSampleRate);
            Console.Write(" Hz");
            return audioSampleRate;
        }
        // For each file path, if file is greater than the max file size, compress.
        static void RotateDataFiles(string airTempDataPath, string airPressureDataPath, string airHumidityDataPath, string enclosureAirTempDataPath, double maxFileSize)
        {

            if (File.Exists(Path.Combine(airTempDataPath, Constants.OutsideAirTemperatureDataFileName)) && new FileInfo(Path.Combine(airTempDataPath, Constants.OutsideAirTemperatureDataFileName)).Length > maxFileSize * 1000)
            {
                CompressionHelper(Path.Combine(airTempDataPath, Constants.OutsideAirTemperatureDataFileName), 20);
            }

            if (File.Exists(Path.Combine(airPressureDataPath, Constants.AirPressureDataFileName)) && new FileInfo(Path.Combine(airPressureDataPath, Constants.AirPressureDataFileName)).Length > maxFileSize * 1000)
            {
                CompressionHelper(Path.Combine(airPressureDataPath, Constants.AirPressureDataFileName), 20);
            }

            if (File.Exists(Path.Combine(airHumidityDataPath, Constants.HumidityDataFileName)) && new FileInfo(Path.Combine(airHumidityDataPath, Constants.HumidityDataFileName)).Length > maxFileSize * 1000)
            {
                CompressionHelper(Path.Combine(airHumidityDataPath, Constants.HumidityDataFileName), 20);
            }

            if (File.Exists(Path.Combine(enclosureAirTempDataPath, Constants.EnclosureAirTemperatureDataFileName)) && new FileInfo(Path.Combine(enclosureAirTempDataPath, Constants.EnclosureAirTemperatureDataFileName)).Length > maxFileSize * 1000)
            {
                CompressionHelper(Path.Combine(enclosureAirTempDataPath, Constants.EnclosureAirTemperatureDataFileName), 20);
            }


        }

        static void CompressionHelper(string filePath, int maxRetainmentPeriodDays)
        {
            // Find all compressed files older than 20 days.
            string directory = Path.GetDirectoryName(filePath);
            string[] allFiles = Directory.GetFiles(directory);
            foreach(string fullFile in allFiles){
                string fileName = new FileInfo(fullFile).Name;
                if(fileName.Split("_").Count() > 1){
                    if(fileName.Split("_")[1].Split(".").Count() > 0){
                        if(DateTime.TryParse(fileName.Split("_")[1].Split(".")[0],out DateTime compressionTime))
                        {
                            if(compressionTime < DateTime.Now.AddDays(-maxRetainmentPeriodDays)){
                                // Delete the file.
                                File.Delete(fullFile);
                            }
                        }
                    }
                }
            }
            string command = $"tar -czf '{filePath + "_" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + ".tar.gz"}' '{filePath}' --remove-files";
            CommandHelper(command);
        }

        static void CommandHelper(string command){
            using (System.Diagnostics.Process proc = new System.Diagnostics.Process())
            {
                proc.StartInfo.FileName = "/bin/bash";
                proc.StartInfo.Arguments = "-c \" " + command + " \"";
                proc.StartInfo.UseShellExecute = false;
                proc.StartInfo.RedirectStandardOutput = true;
                proc.StartInfo.RedirectStandardError = true;
                proc.Start();
                proc.WaitForExit();
            }
        }



    }


}
