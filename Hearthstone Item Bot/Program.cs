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
            
            IRC irc = new IRC();
            irc.StartConnect();
        }
    }
}
