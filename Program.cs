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
        private static string DeviceToConnect = "ArduC";
        // If the device is already connected, it has a different name.
        private static string DeviceToConnect_ConnectedName = "ArduCAM";
        // Camera Trigger/Data characteristic.
        private static string BLECameraCharacteristic = "9a82b386-3169-475a-935a-2394cd7a4d1d";
        // Write this value to BLECameraCharacteristic to trigger camera.
        private static byte[] BLECameraCharacteristicValue_Trigger = new byte[]{0xFF,0xFF,0xFF,0xFF,0xFF,0x74,0x00,0x00};
        // Write this value to BLECameraCharacteristic to fetch packet whose number is replaced by 0xA1 and 0xA2 by the high and low byte respectively.
        private static byte[] BLECameraCharacteristicValue_PacketFetch = new byte[9]{0xFF,0xEF,0xDF,0xCF,0xBF,0xA1,0xA2,0x00,0x00};
        // Temperature Characteristic.
        private static string BLETemperatureCharacteristic = "54eae144-c9b0-448e-9546-facb32a8bc75";
        // Air Pressure Characteristic.
        private static string BLEAirPressureCharacteristic = "6d5c74ff-9853-4350-8970-456607fddcf8";
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
            BLETemperatureCharacteristic,
            BLEAirPressureCharacteristic});
            return characteristics;

        }
        static async Task MainAsync(){
           while(true){
                try{
                    List<IGattCharacteristic1> characteristics = await ConnectToEnvironmentalMonitor();
                    // Get camera characteristic.
                    IGattCharacteristic1 cameraCharacteristic = null;
                    IGattCharacteristic1 temperatureCharacteristic = null;
                    IGattCharacteristic1 airPressureCharacteristic = null;
                    foreach(IGattCharacteristic1 characteristic in characteristics){
                        string UUID = await characteristic.GetUUIDAsync();
                        if(UUID.ToLower() == BLECameraCharacteristic){
                            cameraCharacteristic = characteristic;
                        }
                        if(UUID.ToLower() == BLETemperatureCharacteristic){
                            temperatureCharacteristic = characteristic;
                        }
                        if(UUID.ToLower() == BLEAirPressureCharacteristic){
                            airPressureCharacteristic = characteristic;
                        }
                    }
                    // Stay in a loop.
                    while(true){
                        //await TakeCameraImage(cameraCharacteristic);
                        await ReadTemperature(temperatureCharacteristic);
                        await ReadPressure(airPressureCharacteristic);
                    }
                    MainAsync().Wait();
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
           File.WriteAllBytes("/my/folder/Desktop/pics/" + DateTime.Now.ToString("yyyy_MM_dd hh:mm:ss") + ".jpg", CameraData);
        }

        // Find 0xA1 and 0xA2 and replace with high and low byte of packetToFetch integer.
        static byte[] GetPacketFetchMessage(int packetToFetch){
            byte[] returnMessage = new byte[BLECameraCharacteristicValue_PacketFetch.Length];
            BLECameraCharacteristicValue_PacketFetch.CopyTo(returnMessage, 0);
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
            Console.Write(" °C");
            // TO DO: Where to place reading?
        }
        static async Task ReadPressure(IGattCharacteristic1 pressureCharacteristic){
            byte[] characteristicValue;
            characteristicValue = await pressureCharacteristic.ReadValueAsync(new Dictionary<string, object>());
            int pressure = characteristicValue[0] << 8 |  characteristicValue[1];
            Console.Write("Pressure Reading: ");
            Console.WriteLine(pressure);
            Console.Write(" hPa");
            // TO DO: Where to place reading?
        }



    }
    

}
