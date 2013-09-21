using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IrcDotNet;
using System.Text.RegularExpressions;

namespace hsbot
{
    class IRC : IrcClient
    {
        public IRC()
        {
            RefreshList();
        }

        public void StartConnect()
        {
            var registration = new IrcUserRegistrationInfo();
            registration.NickName = "HearthBot";
            registration.RealName = "Hearthstone IRC Bot";
            registration.UserName = "HearthBot";
            
            

            this.Connect("irc.quakenet.org", false, registration);
            
            this.Registered += new EventHandler<EventArgs>(OnRegistered);

            Console.In.ReadLine();
            
        }
        private void OnRawMessageReceived(IrcRawMessageEventArgs args)
        {
         
            Console.WriteLine(args.RawContent);
        }
        private void OnRegistered(Object sender, EventArgs e)
        {
            
         
            this.SendMessageJoin(new String[] { "#hearthstone" });
            new System.Threading.Thread(() => {

                while (Channels.Count == 0)
                {
                    System.Threading.Thread.Sleep(1000);
                }

                Channels[0].MessageReceived += new System.EventHandler<IrcMessageEventArgs>(OnChannelMessage);

            }).Start();
            
        }
        Regex regex = new Regex(@"\[([^\d][^\]]+)\]");
        private void OnChannelMessage(Object sender, IrcMessageEventArgs e)
        {
            

            if (e.Text.ToLower().StartsWith("!card ") && e.Text.Length > "!card ".Length)
            {
                
                LookupCardNameFor(e.Targets[0], e.Text.Substring("!card ".Length));
            }

            Match match = regex.Match(e.Text);

            for (int i = 0; i < 2 && match.Success; ++i, match = match.NextMatch())
            {
                LookupCardNameFor(e.Targets[0], match.Groups[1].Value);
            }
        }

        private void LookupCardNameFor(IIrcMessageTarget source, String cardname)
        {
            cards.Card c = LookupCard(cardname);
            if (c == null)
                this.Message(source.Name, "The card was not found.");
            else
                Message(source.Name, c.GetFullText().Replace("<b>","").Replace("</b>",""));
        }


        private void Message(String target, String message, params String[] format)
        {
            // God this library sucks ass
            String[] targets = new String[1];
            targets[0] = target;
            SendMessagePrivateMessage(targets, String.Format(message, format));
        }

        private cards.Card LookupCard(String cardname)
        {
            // look for exact match
            cards.Card match;
            if (cards.TryGetValue(cardname.ToLower(), out match))
                return match;
            
            // Otherwise search using contains

            foreach (cards.Card c in cards.Values)
                if (c.Name.ToLower().Contains(cardname)) return c;
            return null;

        }
        private System.Collections.Generic.Dictionary<String, cards.Card> cards = new System.Collections.Generic.Dictionary<String, cards.Card>();
        private void RefreshList()
        {
            var parser = new cards.XMLParser(@"C:\Users\Ben\Desktop\carddata");
            List<cards.Card> list = parser.GetCards();
            cards.Clear();
            foreach (cards.Card c in list)
            {
                try
                {
                    cards.Add(c.Name.ToLower(), c);
                }
                catch (ArgumentException e)
                {
                    Console.Error.WriteLine("Multiple cards have the name \"{0}\".", c.Name);
                }
            }
        }

    }
}
