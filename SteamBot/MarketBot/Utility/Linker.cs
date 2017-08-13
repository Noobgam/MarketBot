using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Utility
{
    public class Linker
    {
        public static void Link(NDota2Market.Dota2Market protocol, NDota2Market.Logic logic, MarketLogger Log)
        {
            if (protocol == null && logic == null)
                Log.Error("Both protocol and linker classes are null in Linker.");
            else if (protocol == null)
                Log.Error("Protocol class is null in Linker.");
            else if (logic == null)
                Log.Error("Logic class is null in Linker.");

            logic.Log = Log;
            protocol.Log = Log;

            logic.Protocol = protocol;
            protocol.Logic = logic;
        }

        public static void Link(CSGOTM.Protocol protocol, CSGOTM.Logic logic, MarketLogger Log)
        {
            if (protocol == null && logic == null)
                Log.Error("Both protocol and linker classes are null in Linker.");
            else if (protocol == null)
                Log.Error("Protocol class is null in Linker.");
            else if (logic == null)
                Log.Error("Logic class is null in Linker.");

            logic.Log = Log;
            protocol.Log = Log;

            logic.Protocol = protocol;
            protocol.Logic = logic;
        }
    }
}
