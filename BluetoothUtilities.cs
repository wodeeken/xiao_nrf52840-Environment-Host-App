using System;
using Tmds.DBus;
using bluez.DBus;
using System.Threading.Tasks;
using System.Diagnostics;
namespace xiao_nrf52840_Environment_Host_App
{

    static class BluetoothUtilities
    {

        public static async Task<IDevice1> ScanAndConnect(string deviceName, string deviceName_Connected)
        {
            IDevice1 device = null;
            // Setup Bluetooth adapter and start discovery.
            var adapter = Connection.System.CreateProxy<IAdapter1>("org.bluez", "/org/bluez/hci0");
            Console.WriteLine("Got Adapter.");
            Console.Write("Adapter address: ");
            Console.WriteLine(await adapter.GetAddressAsync());
            Console.WriteLine("Got Alias: ");
            Console.WriteLine(await adapter.GetAliasAsync());


            // Get devices.
            var objectManager = Connection.System.CreateProxy<IObjectManager>("org.bluez", "/");
            
            bool notYetConnected = true;
            while (notYetConnected)
            {
                try
                {
                    await adapter.StartDiscoveryAsync();
                    System.Threading.Thread.Sleep(2000);
                    var objects = await objectManager.GetManagedObjectsAsync();
                    foreach (var obj in objects)
                    {
                        // For each device obj path, create a device object.
                        if (obj.Key.ToString().Contains("dev"))
                        {
                            var currentDevice = Connection.System.CreateProxy<IDevice1>("org.bluez", obj.Key);
                            try
                            {
                                string deviceAlias = await currentDevice.GetAliasAsync();
                                Console.WriteLine($"Found device: {deviceAlias}");
                                if (deviceAlias == deviceName || deviceAlias == deviceName_Connected)
                                {
                                    Console.WriteLine($"Connecting to {deviceAlias}");
                                    await currentDevice.ConnectAsync();
                                    bool connectSuccess = await currentDevice.GetConnectedAsync();
                                    
                                    if (connectSuccess)
                                    {
                                        notYetConnected = false;
                                        Console.WriteLine("Connected to device.");
                                        await adapter.StopDiscoveryAsync();
                                        device = currentDevice;
                                        break;
                                    }
                                    else
                                        Console.WriteLine("Failed to connect.");
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"Cannot interrogate {obj.Key}");
                            }

                        }

                    }
                    await adapter.StopDiscoveryAsync();
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Exception occurred while scanning devices; {e}");
                }

            }
            return device;
        }
        public static async Task<List<IGattCharacteristic1>> GetCharacteristics(List<string> charUUIDs){
            List<IGattCharacteristic1> returnList = new List<IGattCharacteristic1>();
            // Get object manager
            var objectManager = Connection.System.CreateProxy<IObjectManager>("org.bluez", "/");
            var objects = await objectManager.GetManagedObjectsAsync();
            // Find all service candidates.
            foreach(var obj in objects){
              foreach(var val in obj.Value){
                if(val.Key.Contains("org.bluez.GattCharacteristic1")){
                    // Is the UUID value contained in our list of UUIDs?
                    if(charUUIDs.Contains(val.Value["UUID"])){
                        // create char.
                        var newChar = Connection.System.CreateProxy<IGattCharacteristic1>("org.bluez",obj.Key);
                        returnList.Add(newChar);
                    } 
                }
             }
            }
            return returnList;
        }
        public static async Task<List<IGattService1>> GetServices(List<string> serviceUUIDs){
            List<IGattService1> returnList = new List<IGattService1>();
            // Get object manager
            var objectManager = Connection.System.CreateProxy<IObjectManager>("org.bluez", "/");
            var objects = await objectManager.GetManagedObjectsAsync();
            // Find all service candidates.
            foreach(var obj in objects){
              foreach(var val in obj.Value){
                if(val.Key.Contains("org.bluez.GattService1")){
                    // Is the UUID value contained in our list of UUIDs?
                    if(serviceUUIDs.Contains(val.Value["UUID"])){
                        // create service.
                        var newService = Connection.System.CreateProxy<IGattService1>("org.bluez",obj.Key);
                        returnList.Add(newService);
                    } 
                }
             }
            }
            return returnList;
        }

        

    }
}