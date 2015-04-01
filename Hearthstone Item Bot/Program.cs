using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.IO;

namespace HSBot
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.Title = "HearthBot - Started " + DateTime.Now;
            ThreadPool.SetMaxThreads(100, 100);
            ThreadPool.SetMinThreads(100, 100);
            try
            {
                Config.Reload();
            } catch (Exception)
            {
                Console.Error.WriteLine("Config file corrupt. Please makes sure all the values in config.xml make sense. If you just updated, the structure may have changed - please reconfigure.");
                Console.ReadKey();
                return;
            }
            
            Object hsInstall = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\Software" + (IntPtr.Size == 8 ? @"\Wow6432Node" : "") + @"\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone","InstallLocation",null);

            String cardDataFile = null;

            if (hsInstall != null)
            {
                cardDataFile = System.IO.Path.Combine((String)hsInstall, "Data", "Win", "cardxml0.unity3d");
            }

            if (cardDataFile == null || !System.IO.File.Exists(cardDataFile))
            {
                // Wait 20 seconds on input or proceed in case we are unattended
                Console.WriteLine("Hearthstone installation not found. Enter the absolute path to cardxml0.unity3d or nothing to use the same directory as HSBot.exe");
                String input = null;
                AutoResetEvent inputReceivedEvent = new AutoResetEvent(false);
                Thread getInputThread = new Thread(() => {
                    input = Console.ReadLine();
                    inputReceivedEvent.Set();
                });
                getInputThread.Start();
                inputReceivedEvent.WaitOne(20000);
                if (input == null)
                {
                    // GTFO
                    getInputThread.Interrupt();
                }
                
                if (!String.IsNullOrEmpty(input) && File.Exists(input) && Path.IsPathRooted(input))
                {
                    cardDataFile = input;
                }
                else
                {
                    Console.WriteLine("No or invalid path entered. Defaulting to local cardxml0.unity3d.");
                    cardDataFile = Path.Combine(System.Environment.CurrentDirectory,"cardxml0.unity3d");
                }
            }
            if (cardDataFile == null || !File.Exists(cardDataFile))
            {
                Console.WriteLine("Card data file not found: {0}", cardDataFile);
            }
            else
            {
                Console.WriteLine("I will read card data from {0}", cardDataFile);
                try
                {
                    File.Copy(cardDataFile, "cardxml0.unity3d", true);
                }
                catch (Exception) { }
                
            }

            IRC irc = new IRC(cardDataFile);
         
            irc.StartConnect();

            Console.CancelKeyPress += (s, e) => {
                irc.Client.SendRawMessageAsync("QUIT :Be right back!").Wait(5000);
            };

            while (true)
            {
                new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset).WaitOne();
            }
    
        }
    }
}
