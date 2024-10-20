using System;
using Tmds.DBus;
using bluez.DBus;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
namespace xiao_nrf52840_Environment_Host_App{
    
    class Program{
        // The Alias of the device to connect to.
        private static string DeviceToConnect = "XIAO_";
        // If the device is already connected, it has a different name.
        private static string DeviceToConnect_ConnectedName = "XIAO_MONITOR";
        // Camera Trigger/Data characteristic.
        private static string BLECameraCharacteristic = "9a82b386-3169-475a-935a-2394cd7a4d1d";
        // Audio Trigger/Data characteristic.
        private static string BLEAudioCharacteristic = "e9a68cde-2ac4-4cf4-b920-bf03786aee62";
        private static string BLEAudioSampleRateCharacteristic = "0e70dcba-ced1-47bb-a269-af30eb979f12";
        // Write this value to BLECameraCharacteristic to trigger camera.
        private static byte[] BLECameraCharacteristicValue_Trigger = new byte[]{0xFF,0xFF,0xFF,0xFF,0xFF,0x74,0x00,0x00};
        // Write this value to BLEAudioCharacteristic to trigger microphone.
        private static byte[] BLEAudioCharacteristicValue_Trigger = new byte[]{0xFF,0xFF,0xFF,0xFF,0xFF,0x75,0x00,0x00};
        // Write this value to BLECameraCharacteristic/BLEAudioCharacteristic to fetch packet whose number is replaced by 0xA1 and 0xA2 by the high and low byte respectively.
        private static byte[] BLECamera_AudioCameraCharacteristicValue_PacketFetch = new byte[9]{0xFF,0xEF,0xDF,0xCF,0xBF,0xA1,0xA2,0x00,0x00};
        // Temperature Characteristic.
        private static string BLETemperatureCharacteristic = "54eae144-c9b0-448e-9546-facb32a8bc75";
        // Air Pressure Characteristic.
        private static string BLEAirPressureCharacteristic = "6d5c74ff-9853-4350-8970-456607fddcf8";
        // Humidity Characteristic.
        private static string BLEHumidityCharacteristic = "b2ecd36f-6730-45a8-a5fe-351191642c24";
        private static string BLEHumidityTempCharacteristic = "eec2eb81-ebb1-4352-8420-047304011fdb";
        // Main entry point. 
        static void Main(string[] args)
        {
            // All main code is in MainAsync.
            MainAsync().Wait();
        }

        static async Task<List<IGattCharacteristic1>> ConnectToEnvironmentalMonitor(){
            IDevice1 myDevice = await BluetoothUtilities.ScanAndConnect(DeviceToConnect, DeviceToConnect_ConnectedName);
            // After connecting, find all relevant characteristics (Camera Trigger/Data, Temperature, and Pressure);
            List<IGattCharacteristic1> characteristics = await BluetoothUtilities.GetCharacteristics(new List<string>(){BLECameraCharacteristic, 
            BLEAudioCharacteristic,
            BLEAudioSampleRateCharacteristic,
            BLEAudioSampleRateCharacteristic,
            BLETemperatureCharacteristic,
            BLEAirPressureCharacteristic,
            BLEHumidityCharacteristic,
            BLEHumidityTempCharacteristic});
            return characteristics;

        }
        static async Task MainAsync(){
           while(true){
                try{
                    List<IGattCharacteristic1> characteristics = await ConnectToEnvironmentalMonitor();
                    // Get camera characteristic.
                    IGattCharacteristic1 audioCharacteristic = null;
                    IGattCharacteristic1 audioSampleRateCharacteristic = null;
                    IGattCharacteristic1 cameraCharacteristic = null;
                    IGattCharacteristic1 temperatureCharacteristic = null;
                    IGattCharacteristic1 airPressureCharacteristic = null;
                    IGattCharacteristic1 humidityCharacteristic = null;
                    IGattCharacteristic1 humidityTempCharacteristic = null;
                    foreach(IGattCharacteristic1 characteristic in characteristics){
                        string UUID = await characteristic.GetUUIDAsync();
                        if(UUID.ToLower() == BLECameraCharacteristic){
                            cameraCharacteristic = characteristic;
                        }
                        else if(UUID.ToLower() == BLETemperatureCharacteristic){
                            temperatureCharacteristic = characteristic;
                        }
                        else if(UUID.ToLower() == BLEAirPressureCharacteristic){
                            airPressureCharacteristic = characteristic;
                        }
                        else if(UUID.ToLower() == BLEHumidityCharacteristic){
                            humidityCharacteristic = characteristic;
                        }
                        else if(UUID.ToLower() == BLEHumidityTempCharacteristic){
                            humidityTempCharacteristic = characteristic;
                        }
                        else if(UUID.ToLower() == BLEAudioCharacteristic){
                            audioCharacteristic = characteristic;
                        }else if(UUID.ToLower() == BLEAudioSampleRateCharacteristic){
                            audioSampleRateCharacteristic = characteristic;
                        }
                    }
                    // Stay in a loop.
                    while(true){
                        await TakeCameraImage(cameraCharacteristic);
                        await ReadTemperature(temperatureCharacteristic);
                        await ReadPressure(airPressureCharacteristic);
                        await ReadHumidity(humidityCharacteristic);
                        await ReadHumidityTemp(humidityTempCharacteristic);
                        await RecordMicrophoneAudio(audioCharacteristic, audioSampleRateCharacteristic);
                    }
                }catch(Exception e){

                }
                
            }
        }

        static async Task TakeCameraImage(IGattCharacteristic1 cameraCharacteristic){
           await cameraCharacteristic.WriteValueAsync(BLECameraCharacteristicValue_Trigger,new Dictionary<string, object>() );
           Console.WriteLine("Triggering Camera!");
           // Wait a few seconds for camera to work.
           System.Threading.Thread.Sleep(5000);
           byte[] characteristicValue;
           characteristicValue = await cameraCharacteristic.ReadValueAsync(new Dictionary<string, object>());
           int totalPackets = characteristicValue[0] << 8 |  characteristicValue[1]; 
           byte[] CameraData = new byte[totalPackets * 244];
           for(int currentPacket = 0; currentPacket < totalPackets; currentPacket++){
                Console.Write(currentPacket); Console.WriteLine($" of {totalPackets}");
                await cameraCharacteristic.WriteValueAsync(GetPacketFetchMessage(currentPacket),new Dictionary<string, object>() );
                characteristicValue = await cameraCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                // Stay in loop until we read a valid value.
                while(characteristicValue.Count() <= 1){
                    System.Threading.Thread.Sleep(10);
                    characteristicValue = await cameraCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                }
                // Get the data, save to cam data array.
                int currentValIndex = 0;
                for(int i = currentPacket * 244; i < (currentPacket * 244) + characteristicValue.Count(); i++){
                    CameraData[i] = characteristicValue[currentValIndex];
                    currentValIndex++;
                }
           }
           File.WriteAllBytes("/home/will/Documents/ArduCAMImages/" + DateTime.Now.ToString("yyyy_MM_dd hh:mm:ss") + ".jpg", CameraData);
        }
        static async Task RecordMicrophoneAudio(IGattCharacteristic1 audioCharacteristic, IGattCharacteristic1 audioSampleRateCharacteristic){
            
           await audioCharacteristic.WriteValueAsync(BLEAudioCharacteristicValue_Trigger,new Dictionary<string, object>() );
            
           
           Console.WriteLine("Triggering Microphone!");
           // Wait 20 seconds for audio recording to finish.
           System.Threading.Thread.Sleep(20000);
           byte[] characteristicValue;
           characteristicValue = await audioCharacteristic.ReadValueAsync(new Dictionary<string, object>());
           int totalPackets = characteristicValue[0] << 8 |  characteristicValue[1]; 
           byte[] AudioData = new byte[totalPackets * 244];
           for(int currentPacket = 0; currentPacket < totalPackets; currentPacket++){
                Console.Write(currentPacket); Console.WriteLine($" of {totalPackets}");
                await audioCharacteristic.WriteValueAsync(GetPacketFetchMessage(currentPacket),new Dictionary<string, object>() );
                characteristicValue = await audioCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                // Stay in loop until we read a valid value.
                while(characteristicValue.Count() <= 1){
                    System.Threading.Thread.Sleep(10);
                    characteristicValue = await audioCharacteristic.ReadValueAsync(new Dictionary<string, object>());
                }
                // Get the data, save to data array.
                int currentValIndex = 0;
                for(int i = currentPacket * 244; i < (currentPacket * 244) + characteristicValue.Count(); i++){
                    AudioData[i] = characteristicValue[currentValIndex];
                    currentValIndex++;
                }
           }
           File.WriteAllBytes("/home/will/Documents/ArduCAMImages/" + DateTime.Now.ToString("yyyy_MM_dd hh:mm:ss") + ".audio", AudioData);
           // Wait one second before reading the sample rate characteristic/
           System.Threading.Thread.Sleep(1000);
           int sampleRate = await ReadAudioSampleRate(audioSampleRateCharacteristic);
        }
        // Find 0xA1 and 0xA2 and replace with high and low byte of packetToFetch integer.
        static byte[] GetPacketFetchMessage(int packetToFetch){
            byte[] returnMessage = new byte[BLECamera_AudioCameraCharacteristicValue_PacketFetch.Length];
            BLECamera_AudioCameraCharacteristicValue_PacketFetch.CopyTo(returnMessage, 0);
            ushort curPacket_short = Convert.ToUInt16(packetToFetch);
            for(int i = 0; i < returnMessage.Length; i++){
                if(returnMessage[i].CompareTo(0xA1) == 0){
                    returnMessage[i] = (byte) (curPacket_short >> 8);
                }
                else if(returnMessage[i].CompareTo(0xA2) == 0) {
                    returnMessage[i] = (byte) (curPacket_short & 0xff);
                }
                    
            }
            return returnMessage;
        }
        static async Task ReadTemperature(IGattCharacteristic1 tempCharacteristic){
            byte[] characteristicValue;
            characteristicValue = await tempCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            // Temp is rounded to nearest integer.
            int temp = characteristicValue[0] << 8 |  characteristicValue[1];
            Console.Write("Temp Reading: ");
            Console.Write(temp);
            Console.WriteLine(" °C");
            // TO DO: Where to place reading?
        }
        static async Task ReadPressure(IGattCharacteristic1 pressureCharacteristic){
            byte[] characteristicValue;
            characteristicValue = await pressureCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int pressure = characteristicValue[0] << 8 |  characteristicValue[1];
            Console.Write("Pressure Reading: ");
            Console.Write(pressure);
            Console.WriteLine(" hPa");
            // TO DO: Where to place reading?
        }
        static async Task ReadHumidity(IGattCharacteristic1 humidityCharacteristic){
            byte[] characteristicValue;
            characteristicValue = await humidityCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int humidity = characteristicValue[0] << 8 |  characteristicValue[1];
            Console.Write("Humidity Reading: ");
            Console.Write(humidity);
            Console.WriteLine(" %RH");
            // TO DO: Where to place reading?
        }
        static async Task ReadHumidityTemp(IGattCharacteristic1 humidityTempCharacteristic){
            byte[] characteristicValue;
            characteristicValue = await humidityTempCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int humidityTemp = characteristicValue[0] << 8 |  characteristicValue[1];
            Console.Write("Temp used in Humidity Reading: ");
            Console.WriteLine(humidityTemp);
            Console.Write(" °C");
            // TO DO: Where to place reading?
        }
        static async Task<int> ReadAudioSampleRate(IGattCharacteristic1 audioSampleRateCharacteristic){
            byte[] characteristicValue;
            characteristicValue = await audioSampleRateCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int audioSampleRate = characteristicValue[0] << 8 |  characteristicValue[1];
            Console.Write("Audio Sample Rate: ");
            Console.WriteLine(audioSampleRate);
            Console.Write(" Hz");
            return audioSampleRate;
        }



    }
    

}
