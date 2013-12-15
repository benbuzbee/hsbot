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
            
            
            
            
            Config.Reload();
            
            Object hsInstall = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone","InstallLocation",null);

            String cardData = null;

            if (hsInstall != null)
            {
                cardData = System.IO.Path.Combine((String)hsInstall, "Data", "Win", "cardxml0.unity3d");
            }
            if (cardData == null || !System.IO.File.Exists(cardData))
            {
                Console.WriteLine("Hearthstone installation not found. Enter the path to cardxml0.unity3d or enter nothing to continue without extracting new card data.");
                String input = Console.ReadLine();
                if (!String.IsNullOrEmpty(input) && System.IO.File.Exists(input))
                    cardData = input;
                else
                    cardData = null;
            }
            if (cardData != null && System.IO.File.Exists(cardData))
                Cards.FileExtractor.Extract(cardData,Config.DataDirectory);

            IRC irc = new IRC();
            irc.StartConnect();
            Console.CancelKeyPress += (s, e) => {
                irc.Client.SendRawMessage("QUIT :Be right back!").Wait(5000);
            };


            new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.ManualReset).WaitOne();
    
        }
    }
}
