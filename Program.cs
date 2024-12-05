using System;
using Tmds.DBus;
using bluez.DBus;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Data;
using System.Globalization;
namespace xiao_nrf52840_Environment_Host_App
{

    class Program
    {
        // Main entry point. 
        static void Main(string[] args)
        {
            double maxFileSize;
            int retainmentPeriodDays;
            int dataReadIntervalMinutes;
            if (args.Count() < 4)
            {
                throw new ArgumentException("No arguments passed. Expecting 4 in the following order: " +
                "{Data directory, " + 
                "max air monitor data file size in Kb (triggers archiving), data retainment in days (valid for archived air monitor data and image/audio files), data read interval (in minutes) }.");
            }
            else
            {
                
                if (!Directory.Exists(args[0]))
                {
                    throw new ArgumentException($"Arg path at index {0} ({args[0]}) does not point to a valid directory.");
                }
                if (!double.TryParse(args[args.Count() - 3], out maxFileSize) || maxFileSize < 1)
                {
                    throw new ArgumentException($"Arg Max file size at index {args.Count() - 3} ({args[args.Count() - 3]}) is not in proper format (cannot be less than 1 kb )");
                }
                if (!int.TryParse(args[args.Count() - 2], out retainmentPeriodDays) || retainmentPeriodDays < 1)
                {
                    throw new ArgumentException($"Arg data retainment in days at index {args.Count() - 2} ({args[args.Count() - 2]}) is not in proper format (must be an integer >= 1 days)");
                }
                if (!int.TryParse(args[args.Count() - 1], out dataReadIntervalMinutes) || dataReadIntervalMinutes < 1)
                {
                    throw new ArgumentException($"Arg data fetch interval in minutes at index {args.Count() - 1} ({args[args.Count() - 1]}) is not in proper format (must be an integer >= 1 minutes)");
                }
            }
            // All main code is in MainAsync.
            MainAsync(Path.Combine(args[0], Constants.CameraDataFolderName), Path.Combine(args[0], Constants.AudioDataFolderName),
             Path.Combine(args[0], Constants.OutsideAirTemperatureDataFolderName), 
             Path.Combine(args[0], Constants.AirPressureDataFolderName), 
             Path.Combine(args[0], Constants.RelativeHumidityFolderName), 
             Path.Combine(args[0], Constants.EnclosureDataFolderName), maxFileSize, retainmentPeriodDays, dataReadIntervalMinutes).Wait();
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
        static async Task MainAsync(string cameraDataPath, string audioDataPath, string airTempDataPath, string airPressureDataPath, 
                    string airHumidityDataPath, string enclosureAirTempDataPath, double maxFileSize, int retainmentPeriodDays, int dataIntervalMinutes)
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
                    DateTime lastDataReadTime_Begin;
                    // Stay in a loop.
                    while (true)
                    {
                        lastDataReadTime_Begin = DateTime.UtcNow;
                        // Archive/Delete old data according to file size (air monitor data) and retainment period (air monitor archives and audio/camera data).
                        RotateDataFiles(airTempDataPath, airPressureDataPath, airHumidityDataPath, enclosureAirTempDataPath, cameraDataPath, audioDataPath, maxFileSize, retainmentPeriodDays);
                        await ReadEnclosureTemperature(enclosureTemperatureCharacteristic, enclosureAirTempDataPath);
                        await ReadPressure(airPressureCharacteristic, airPressureDataPath);
                        await ReadHumidity(humidityCharacteristic, airHumidityDataPath);
                        await ReadTemperature(temperatureCharacteristic, airTempDataPath);
                        await TakeCameraImage(cameraCharacteristic, cameraDataPath);
                        await RecordMicrophoneAudio(audioCharacteristic, audioSampleRateCharacteristic, audioDataPath);

                        // Calculate next read time. If we've already exceeded the interval time, don't delay.
                        if(lastDataReadTime_Begin.AddMinutes(dataIntervalMinutes) > DateTime.UtcNow){
                            double waitPeriod = (lastDataReadTime_Begin.AddMinutes(dataIntervalMinutes) - DateTime.UtcNow).TotalMilliseconds;
                            Console.WriteLine($"Now sleeping for {(int) waitPeriod} milliseconds until time for next read.");
                            System.Threading.Thread.Sleep((int) waitPeriod);
                        }
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
            byte[] characteristicValue = [];
            bool shouldWait = true;
            int maxLoopWait = 1000;
            int curLoopWait = 0;
            while(shouldWait && curLoopWait < maxLoopWait){
                characteristicValue = await cameraCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                // Wait until characteristic != trigger.
                if(characteristicValue.Count() < 8 || (characteristicValue[0] ==  Constants.BLECameraCharacteristicValue_Trigger[0] 
                && characteristicValue[1] == Constants.BLECameraCharacteristicValue_Trigger[1] 
                && characteristicValue[2] == Constants.BLECameraCharacteristicValue_Trigger[2] 
                && characteristicValue[3] == Constants.BLECameraCharacteristicValue_Trigger[3]
                && characteristicValue[4] == Constants.BLECameraCharacteristicValue_Trigger[4]
                && characteristicValue[5] == Constants.BLECameraCharacteristicValue_Trigger[5]
                && characteristicValue[6] == Constants.BLECameraCharacteristicValue_Trigger[6]
                && characteristicValue[7] == Constants.BLECameraCharacteristicValue_Trigger[7]))
                {
                    Console.WriteLine($"Waiting for camera capture to complete: {curLoopWait} of {maxLoopWait}");
                    System.Threading.Thread.Sleep(2000);
                    
                }else{
                    shouldWait = false;
                }
                curLoopWait++;

            }
            if(curLoopWait >= maxLoopWait){
                Console.Write("Audio wait timeout. Skipping data transmission.");
                return;
            }
            int totalPackets = characteristicValue[0] << 8 | characteristicValue[1];
            byte[] CameraData = new byte[totalPackets * 244];
            for (int currentPacket = 0; currentPacket < totalPackets; currentPacket++)
            {
                Console.Write(currentPacket); Console.WriteLine($" of {totalPackets}");
                try{
                    await cameraCharacteristic.WriteValueAsync(GetPacketFetchMessage(currentPacket), new Dictionary<string, object>());
                
                    characteristicValue = await cameraCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                }catch(Exception e){
                    if(e.Message == "org.bluez.Error.Failed: Operation failed with ATT error: 0x0e"){
                        currentPacket--;
                        System.Threading.Thread.Sleep(1000);
                        continue;
                    }else{
                        throw e;
                    }
            
                }
                
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
            byte[] characteristicValue = [];
            bool shouldWait = true;
            int maxLoopWait = 1000;
            int curLoopWait = 0;
            while(shouldWait && curLoopWait < maxLoopWait){
                characteristicValue = await audioCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                // Wait until characteristic != trigger.
                if(characteristicValue.Count() < 8 || (characteristicValue[0] ==  Constants.BLEAudioCharacteristicValue_Trigger[0] 
                && characteristicValue[1] == Constants.BLEAudioCharacteristicValue_Trigger[1] 
                && characteristicValue[2] == Constants.BLEAudioCharacteristicValue_Trigger[2] 
                && characteristicValue[3] == Constants.BLEAudioCharacteristicValue_Trigger[3]
                && characteristicValue[4] == Constants.BLEAudioCharacteristicValue_Trigger[4]
                && characteristicValue[5] == Constants.BLEAudioCharacteristicValue_Trigger[5]
                && characteristicValue[6] == Constants.BLEAudioCharacteristicValue_Trigger[6]
                && characteristicValue[7] == Constants.BLEAudioCharacteristicValue_Trigger[7]))
                {
                    Console.WriteLine($"Waiting for audio capture to complete: {curLoopWait} of {maxLoopWait}");
                    System.Threading.Thread.Sleep(2000);
                }else{
                    shouldWait = false;
                }
                curLoopWait++;

            }
            if(curLoopWait >= maxLoopWait){
                Console.Write("Audio wait timeout. Skipping data transmission.");
                return;
            }
            int totalPackets = characteristicValue[0] << 8 | characteristicValue[1];
            byte[] AudioData = new byte[totalPackets * 244];
            for (int currentPacket = 0; currentPacket < totalPackets; currentPacket++)
            {
                Console.Write(currentPacket); Console.WriteLine($" of {totalPackets}");
                try{
                    await audioCharacteristic.WriteValueAsync(GetPacketFetchMessage(currentPacket), new Dictionary<string, object>());
                    characteristicValue = await audioCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                }catch(Exception e){
                    if(e.Message == "org.bluez.Error.Failed: Operation failed with ATT error: 0x0e"){
                        currentPacket--;
                        System.Threading.Thread.Sleep(1000);
                        continue;
                    }else{
                        throw e;
                    }
                }
                
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
            int temp = characteristicValue[0] << 24 | characteristicValue[1] << 16 | characteristicValue[2] << 8 | characteristicValue[3];
            Console.Write("Enclosure Temperature Reading: ");
            Console.Write(temp);
            Console.WriteLine(" °C");
            double toFahr = Utilities.ConvertCelsiusToFahrenheit(temp);
            string dataString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " = " + toFahr.ToString("0.0") + "°F/" +  temp.ToString() + "°C" + Environment.NewLine;
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
            string dataString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " = " + pressure.ToString() + " hPa" + Environment.NewLine;
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
            string dataString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " = " + humidity.ToString() + "%" + Environment.NewLine;
            File.AppendAllText(Path.Combine(humidityDataPath, Constants.HumidityDataFileName), dataString);
        }
        static async Task ReadTemperature(IGattCharacteristic1 tempCharacteristic, string temperatureDataPath)
        {
            byte[] characteristicValue;
            characteristicValue = await tempCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int temp = characteristicValue[0] << 24 | characteristicValue[1] << 16 | characteristicValue[2] << 8 | characteristicValue[3];
            Console.Write("Temperature Reading: ");
            Console.WriteLine(temp);
            double toFahr = Utilities.ConvertCelsiusToFahrenheit(temp);
            Console.Write(" °C");
            string dataString = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz") + " = " + toFahr.ToString("0.0") + "°F/" + temp.ToString() + "°C" + Environment.NewLine;
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
        static void RotateDataFiles(string airTempDataPath, string airPressureDataPath, string airHumidityDataPath, string enclosureAirTempDataPath, 
                string cameraDataPath, string audioDataPath, double maxFileSize, int retainmentPeriodDays)
        {
            if(!File.Exists(audioDataPath)){
                Directory.CreateDirectory(audioDataPath);
            }
            if(!File.Exists(cameraDataPath)){
                Directory.CreateDirectory(cameraDataPath);
            }
            if(!File.Exists(airTempDataPath)){
                Directory.CreateDirectory(airTempDataPath);
            }
            if(!File.Exists(airPressureDataPath)){
                Directory.CreateDirectory(airPressureDataPath);
            }
            if(!File.Exists(airHumidityDataPath)){
                Directory.CreateDirectory(airHumidityDataPath);
            }
            if(!File.Exists(enclosureAirTempDataPath)){
                Directory.CreateDirectory(enclosureAirTempDataPath);
            }
            // Delete old audio.
            DeleteOldDataFiles(audioDataPath, retainmentPeriodDays);
            // Delete old images.
            DeleteOldDataFiles(cameraDataPath, retainmentPeriodDays);
            if (File.Exists(Path.Combine(airTempDataPath, Constants.OutsideAirTemperatureDataFileName)) && new FileInfo(Path.Combine(airTempDataPath, Constants.OutsideAirTemperatureDataFileName)).Length > maxFileSize * 1000)
            {
                CompressionHelper(Path.Combine(airTempDataPath, Constants.OutsideAirTemperatureDataFileName), retainmentPeriodDays);
            }

            if (File.Exists(Path.Combine(airPressureDataPath, Constants.AirPressureDataFileName)) && new FileInfo(Path.Combine(airPressureDataPath, Constants.AirPressureDataFileName)).Length > maxFileSize * 1000)
            {
                CompressionHelper(Path.Combine(airPressureDataPath, Constants.AirPressureDataFileName), retainmentPeriodDays);
            }

            if (File.Exists(Path.Combine(airHumidityDataPath, Constants.HumidityDataFileName)) && new FileInfo(Path.Combine(airHumidityDataPath, Constants.HumidityDataFileName)).Length > maxFileSize * 1000)
            {
                CompressionHelper(Path.Combine(airHumidityDataPath, Constants.HumidityDataFileName), retainmentPeriodDays);
            }

            if (File.Exists(Path.Combine(enclosureAirTempDataPath, Constants.EnclosureAirTemperatureDataFileName)) && new FileInfo(Path.Combine(enclosureAirTempDataPath, Constants.EnclosureAirTemperatureDataFileName)).Length > maxFileSize * 1000)
            {
                CompressionHelper(Path.Combine(enclosureAirTempDataPath, Constants.EnclosureAirTemperatureDataFileName), retainmentPeriodDays);
            }


        }
        // Helper for deleting old files whose name matches the format "yyyy_MM_dd hh:mm:ss.<ext>"
        static void DeleteOldDataFiles(string filePath, int maxRetainmentPeriodDays){
            // Find all compressed files older than maxRetainmentPeriod days.
            string[] allFiles = Directory.GetFiles(filePath);
            foreach(string fullFile in allFiles){
                string fileName = new FileInfo(fullFile).Name;
                if(fileName.Split(".").Count() > 0){

                    if(DateTime.TryParseExact(fileName.Split(".")[0],"yyyy_MM_dd HH:mm:ss", 
                    CultureInfo.InvariantCulture, DateTimeStyles.None,out DateTime compressionTime))
                    {
                        if(compressionTime < DateTime.Now.AddDays(-maxRetainmentPeriodDays)){
                            // Delete the file.
                            File.Delete(fullFile);
                        }
                    }
                }
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
