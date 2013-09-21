using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace hsbot
{
    class Program
    {
        static void Main(string[] args)
        {
            IRC irc = new IRC();
            irc.StartConnect();
        }
    }
}
