using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace HSBot.Cards
{
    class XMLParser
    {
        public String Directory { private set; get; }
        /**
         * Parses item xml files in the given directory
         * */
        public XMLParser(String directory)
        {
            Directory = directory;
        }

       
    }
}
