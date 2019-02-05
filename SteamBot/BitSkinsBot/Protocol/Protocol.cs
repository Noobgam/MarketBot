using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitSkins {
    public class Protocol {
        PusherSocket socket;
        public Protocol() {
            //Pusher.Pusher
            socket = new PusherSocket();
        }
    }
}
