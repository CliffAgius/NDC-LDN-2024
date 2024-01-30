using Meadow;
using Meadow.Devices;
using Meadow.Foundation;
using Meadow.Foundation.Leds;
using Meadow.Foundation.Servos;
using Meadow.Foundation.Web.Maple;
using Meadow.Hardware;
using Meadow.Peripherals.Leds;
using Meadow.Units;
using MeadowRobot.Controllers;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace MeadowRobot
{
    // Change F7FeatherV2 to F7FeatherV1 for V1.x boards
    public class MeadowApp : App<F7FeatherV1>
    {
        RgbPwmLed onboardLed;
        MapleServer mapleServer;

        public override Task Initialize()
        {
            Resolver.Log.Info("Initialize...");

            onboardLed = new RgbPwmLed(
                redPwmPin: Device.Pins.OnboardLedRed,
                greenPwmPin: Device.Pins.OnboardLedGreen,
                bluePwmPin: Device.Pins.OnboardLedBlue,
                CommonType.CommonAnode);

            ServoController.Instance.RotateTo(new Angle(NamedServoConfigs.SG90.MinimumAngle));

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

                // set-up and start the Maple server.
                mapleServer = new MapleServer(networkAdapter.IpAddress, 5417, true, logger: Resolver.Log);
                mapleServer.Start();
                // change the LED color to green to show we are connected.
                onboardLed.SetColor(Color.Green);
            };
            // disconnect event
            wifi.NetworkDisconnected += sender => {
                Resolver.Log.Info($"Network disconnected.");
            };

            return base.Initialize();
        }
    }
}