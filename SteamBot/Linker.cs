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
            Utility.MarketLogger Log = logic.Log;
            if (protocol == null && logic == null)
                Log.Error("Both protocol and linker classes are null in Linker.");
            else if (protocol == null)
                Log.Error("Protocol class is null in Linker.");
            else if (logic == null)
                Log.Error("Logic class is null in Linker.");

            logic.Protocol = protocol;
            protocol.Logic = logic;
        }
    }
}
