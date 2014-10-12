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
            

            


            Config.Reload();
            
            Object hsInstall = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone","InstallLocation",null);

            String cardData = null;

            if (hsInstall != null)
            {
                cardData = System.IO.Path.Combine((String)hsInstall, "Data", "Win", "cardxml0.unity3d");
            }

            if (cardData == null || !System.IO.File.Exists(cardData))
            {
                Console.WriteLine("Hearthstone installation not found. Enter the path to cardxml0.unity3d or enter to use the same directory as HSBot.exe");
                String input = Console.ReadLine();
                if (!String.IsNullOrEmpty(input) && System.IO.File.Exists(input))
                    cardData = input;
                else
                    cardData = "cardxml0.unity3d";
            }
            if (cardData == null || !System.IO.File.Exists(cardData))
            {
                Console.WriteLine("Card data file not found: {0}", cardData);
            }
            else
            {
                Console.WriteLine("I will read card data from {0}", cardData);
                try
                {
                    System.IO.File.Copy(cardData, "cardxml0.unity3d");
                }
                catch (Exception) { }
                
            }

            IRC irc = new IRC(cardData);
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
