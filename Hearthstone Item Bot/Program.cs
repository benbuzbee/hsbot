using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HSBot
{
    class Program
    {
        static void Main(string[] args)
        {

            Console.Title = "HearthBot - Started " + DateTime.Now;




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

            String szCardDataFile = null;

            if (hsInstall != null)
            {
                szCardDataFile = System.IO.Path.Combine((String)hsInstall, "Data", "Win", "cardxml0.unity3d");
            }

            if (szCardDataFile == null || !System.IO.File.Exists(szCardDataFile))
            {
                Console.WriteLine("Hearthstone installation not found. Enter the path to cardxml0.unity3d or enter to use the same directory as HSBot.exe");
                String input = Console.ReadLine();
                if (!String.IsNullOrEmpty(input) && System.IO.File.Exists(input))
                {
                    szCardDataFile = input;
                }
                else
                {
                    szCardDataFile = "cardxml0.unity3d";
                }
            }
            if (szCardDataFile == null || !System.IO.File.Exists(szCardDataFile))
            {
                Console.WriteLine("Card data file not found: {0}", szCardDataFile);
            }
            else
            {
                Console.WriteLine("I will read card data from {0}", szCardDataFile);
                try
                {
                    System.IO.File.Copy(szCardDataFile, "cardxml0.unity3d");
                }
                catch (Exception) { }
                
            }

            IRC irc = new IRC(szCardDataFile);
            // Temp:
            irc.Client.OnRfcPrivmsg += (sender, source, target, message) =>
            {
                if (message.StartsWith("@dumpusers "))
                {
                    benbuzbee.LRTIRC.Channel channel = sender.GetChannel(message.Split(' ')[1]);
                    IEnumerable<benbuzbee.LRTIRC.ChannelUser> users = channel.Users.Values.OrderBy<benbuzbee.LRTIRC.ChannelUser, String>((usr) => usr.Prefixes + usr.Nick);
                    foreach (var usr in users)
                    {
                        Console.WriteLine("{0}{1}", usr.Prefixes, usr.Nick);
                    }
                }
            };
            irc.StartConnect();
            Console.CancelKeyPress += (s, e) => {
                irc.Client.SendRawMessage("QUIT :Be right back!").Wait(5000);
            };

            while (true)
            {
                new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset).WaitOne();
            }
    
        }
    }
}
