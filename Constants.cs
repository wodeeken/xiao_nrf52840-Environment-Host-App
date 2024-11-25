namespace xiao_nrf52840_Environment_Host_App{
    
    public static class Constants{
        // Bluetooth UUIDS
        // - UUIDS of the Bluetooth Device Aliases/Services/Characteristics.


        // The Alias of the device to connect to.
        public const string DeviceToConnect = "XIAO_";
        // If the device is already connected, it has a different name.
        public const string DeviceToConnect_ConnectedName = "XIAO_MONITOR";
        // Camera Trigger/Data characteristic.
        public const string BLECameraCharacteristic = "9a82b386-3169-475a-935a-2394cd7a4d1d";
        // Audio Trigger/Data characteristic.
        public const string BLEAudioCharacteristic = "e9a68cde-2ac4-4cf4-b920-bf03786aee62";
        public const string BLEAudioSampleRateCharacteristic = "0e70dcba-ced1-47bb-a269-af30eb979f12";
        // Inside Enclosure Temperature Characteristic.
        public const string BLEEnclosureTemperatureCharacteristic = "54eae144-c9b0-448e-9546-facb32a8bc75";
        // Air Pressure Characteristic.
        public const string BLEAirPressureCharacteristic = "6d5c74ff-9853-4350-8970-456607fddcf8";
        // Humidity Characteristic.
        public const string BLEHumidityCharacteristic = "b2ecd36f-6730-45a8-a5fe-351191642c24";
        // Outside Temperature Characteristic.
        public const string BLETemperatureCharacteristic = "eec2eb81-ebb1-4352-8420-047304011fdb";

        // Bluetooth Camera/Audio Commands
        // - Write this value to BLECameraCharacteristic to trigger camera.
        public static readonly byte[] BLECameraCharacteristicValue_Trigger =  {0xFF,0xFF,0xFF,0xFF,0xFF,0x74,0x00,0x00};
        // Write this value to BLEAudioCharacteristic to trigger microphone.
        public static readonly byte[] BLEAudioCharacteristicValue_Trigger = {0xFF,0xFF,0xFF,0xFF,0xFF,0x75,0x00,0x00};
        // Write this value to BLECameraCharacteristic/BLEAudioCharacteristic to fetch packet whose number is replaced by 0xA1 and 0xA2 by the high and low byte respectively.
        public static readonly byte[] BLECamera_AudioCameraCharacteristicValue_PacketFetch = {0xFF,0xEF,0xDF,0xCF,0xBF,0xA1,0xA2,0x00,0x00};

        // Air Monitor File Names
        // - Names of the air monitor data files.
        public const string EnclosureDataFolderName = "EnclosureTemperatureData";
        public const string EnclosureAirTemperatureDataFileName = "EnclosureTemp.data";
        public const string OutsideAirTemperatureDataFolderName = "AirTemperatureData";
        public const string OutsideAirTemperatureDataFileName = "Temperature.data";
        public const string AirPressureDataFolderName = "AirPressureData";
        public const string AirPressureDataFileName = "Pressure.data";
        public const string RelativeHumidityFolderName = "RelativeHumidityData";
        public const string HumidityDataFileName = "Humidity.data";
        public const string CameraDataFolderName = "CameraData";
        public const string AudioDataFolderName = "AudioData";
    }
    
    }