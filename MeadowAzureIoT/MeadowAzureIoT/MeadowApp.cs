using Amqp;
using Meadow;
using Meadow.Devices;
using Meadow.Foundation;
using Meadow.Foundation.Leds;
using Meadow.Gateway.WiFi;
using Meadow.Hardware;
using Meadow.Peripherals.Leds;
using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace MeadowAzureIoT
{
    // Change F7FeatherV2 to F7FeatherV1 for V1.x boards
    public class MeadowApp : App<F7FeatherV1>
    {
        private static readonly Random rand = new Random();

        static RgbPwmLed onboardLed;

        static bool isRunning = true;

        //You'll need to create an IoT Hub - https://docs.microsoft.com/en-us/azure/iot-hub/iot-hub-create-through-portal?WT.mc_id=AZ-MVP-5003764
        const string HubName = "CAS-Learning-IOTHub";
        //Create a device within your hub
        const string DeviceId = "MeadowF7V1_AzureIoT";
        //And then generate a SAS token - this can be done via the Azure CLI 
        //Example: az iot hub generate-sas-token --hub-name MeadowIoTHub --device-id MeadowF7
        const string SasToken = "SharedAccessSignature sr=CAS-Learning-IOTHub.azure-devices.net%2Fdevices%2FMeadowF7V1_AzureIoT&sig=HjLnb9BaswT421FRUsXEkK4ZE6Pl%2BZ12KHgNJ%2FMvHNA%3D&se=1715187998";

        // Lat/Lon Points
        // NDC London location - 51.50035364615284, -0.12902958630742073
        static double latitude = 51.50035364615284;
        static double longitude = -0.12902958630742073;
        const double radius = 6378;

        static readonly bool traceOn = false;

        public override async Task Initialize()
        {
            try
            {
                Resolver.Log.Info("Initialize...");

                onboardLed = new RgbPwmLed(
                    redPwmPin: Device.Pins.OnboardLedRed,
                    greenPwmPin: Device.Pins.OnboardLedGreen,
                    bluePwmPin: Device.Pins.OnboardLedBlue,
                    CommonType.CommonAnode);

                // connnect to the wifi network.
                SetupNetworking();

                onboardLed.SetColor(Color.Red);

                Console.WriteLine("Amqp setup");

                // setup AMQP
                Amqp.Trace.TraceLevel = Amqp.TraceLevel.Frame | Amqp.TraceLevel.Information;
                // enable trace
                Amqp.Trace.TraceListener = WriteTrace;
                Connection.DisableServerCertValidation = false;

                Console.WriteLine("Start thread");

                // launch worker thread
                await WorkerThread();

                Console.WriteLine("Thread Ended!!!");
            }
            catch (Exception ex)
            {
                onboardLed.SetColor(Color.Red);
                Console.WriteLine($"-- Initialize Error - {ex.Message} --");
            }
        }

        async Task WorkerThread()
        {
            try
            {
                // parse Azure IoT Hub Map settings to AMQP protocol settings
                string hostName = HubName + ".azure-devices.net";
                string userName = DeviceId + "@sas." + HubName;
                string senderAddress = "devices/" + DeviceId + "/messages/events";
                string receiverAddress = "devices/" + DeviceId + "/messages/deviceBound";
                var address = new Address(hostName, 5671, userName, SasToken);
                var connection = new Connection(address);
                var session = new Session(connection);
                var sender = new SenderLink(session, "send-link", senderAddress);
                var receiver = new ReceiverLink(session, "receive-link", receiverAddress);
                receiver.Start(100, OnMessage);

                while(isRunning)
                {
                    // *****************
                    // These are here due AMQP Device connection issue...
                    connection = new Connection(address);
                    session = new Session(connection);
                    sender = new SenderLink(session, "send-link", senderAddress);
                    // ******************

                    onboardLed.SetColor(Color.Blue);

                    // update the location data
                    Console.WriteLine("Update location data");

                    UpdateMockDestination();

                    Console.WriteLine("Create payload");
                    string messagePayload = $"{{\"Latitude\":{latitude},\"Longitude\":{longitude}}}";

                    // compose message
                    Console.WriteLine("Create message");
                    Message message = new Message(Encoding.UTF8.GetBytes(messagePayload));
                    message.ApplicationProperties = new Amqp.Framing.ApplicationProperties();

                    // send message with the new Lat/Lon
                    Console.WriteLine("Send message");
                    Console.WriteLine($"Session State - {session.SessionState.ToString()}");
                    sender.Send(message);
                    // data sent
                    Console.WriteLine($"*** DATA SENT - Lat - {latitude}, Lon - {longitude} ***");
                }

                Console.WriteLine("Finished sending data...");
                await sender.CloseAsync();
                await receiver.CloseAsync();
                await session.CloseAsync();
                await connection.CloseAsync();
            }
            catch (Exception ex)
            {
                onboardLed.SetColor(Color.Red);
                Console.WriteLine($"-- Worker Thread Error - {ex.Message} --");
            }
        }

        private static void OnMessage(IReceiverLink receiver, Message message)
        {
            Console.WriteLine("Message received");

            try
            {   // command received 
                double.TryParse((string)message.ApplicationProperties["setlat"], out latitude);
                double.TryParse((string)message.ApplicationProperties["setlon"], out longitude);

                Console.WriteLine($"== Received new Location setting: Lat - {latitude}, Lon - {longitude} ==");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"-- C2D Error - {ex.Message} --");
            }
        }

        static void WriteTrace(Amqp.TraceLevel level, string format, params object[] args)
        {
            if (traceOn)
            {
                Console.WriteLine(Fx.Format(format, args));
            }
        }

        // Starting at the last Lat/Lon move along the bearing and for the distance to reset the Lat/Lon at a new point...
        public static void UpdateMockDestination()
        {
            // Get a random Bearing and Distance...
            double distance = rand.Next(10);     // Random distance from 0 to 10km...
            double bearing = rand.Next(360);     // Random bearing from 0 to 360 degrees...

            // Haversine Calculation...
            double lat1 = latitude * (Math.PI / 180);
            double lon1 = longitude * (Math.PI / 180);
            double brng = bearing * (Math.PI / 180);
            double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(distance / radius) + Math.Cos(lat1) * Math.Sin(distance / radius) * Math.Cos(brng));
            double lon2 = lon1 + Math.Atan2(Math.Sin(brng) * Math.Sin(distance / radius) * Math.Cos(lat1), Math.Cos(distance / radius) - Math.Sin(lat1) * Math.Sin(lat2));

            latitude = lat2 * (180 / Math.PI);
            longitude = lon2 * (180 / Math.PI);
        }

        private void SetupNetworking()
        {
            // get the wifi adapter
            var wifi = Resolver.Device.NetworkAdapters.Primary<IWiFiNetworkAdapter>();
            // set initial state
            if (wifi.IsConnected)
            {
                Resolver.Log.Info("Already connected to WiFi.");
            }
            else
            {
                Resolver.Log.Info("Not connected to WiFi yet.");
            }
            // connect event
            wifi.NetworkConnected += (networkAdapter, networkConnectionEventArgs) =>
            {
                Resolver.Log.Info($"Joined network - IP Address: {networkAdapter.IpAddress}");

                // change the LED color to green to show we are connected.
                onboardLed.SetColor(Color.Green);
            };
            // disconnect event
            wifi.NetworkDisconnected += sender =>
            {
                Resolver.Log.Info($"Network disconnected.");
            };
        }
    }
}