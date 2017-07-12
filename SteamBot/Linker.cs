using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSGOTM
{
    public class Linker
    {
        public static void Link(CSGOTMProtocol protocol, Logic logic)
        {
            if (protocol == null || logic == null)
                Console.WriteLine("Atention! Atention! Atention! Atention! Atention!");
            logic.Protocol = protocol;
            protocol.Logic = logic;
        }
    }
}
