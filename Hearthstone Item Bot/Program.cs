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
            IRC irc;
            DateTime lastMessage = new DateTime(1);
            // A hacked together reconnect check loop to fit a hacked together IRC library.
            new System.Threading.Thread(
                    new System.Threading.ThreadStart(
                        () => {
                            while (true)
                            {
                                if ((DateTime.Now - lastMessage) > TimeSpan.FromMinutes(2))
                                {
                                    Console.WriteLine("Not connected...attempting to connect...");
                                    lastMessage = DateTime.Now;
                                    irc = new IRC();
                                    irc.StartConnect();
                                    irc.RawMessageReceived += new EventHandler<IrcDotNet.IrcRawMessageEventArgs>((arguments, sender) =>
                                    {
                                        lastMessage = DateTime.Now;
                                    });
                                }
                                System.Threading.Thread.Sleep(2 * (60 * 1000));
                                

                            }
                        
                        }
                        )
                
                ).Start();

           
        }
    }
}
