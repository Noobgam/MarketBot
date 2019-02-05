using PusherClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BitSkins {
    public static class Consts {

        public static class Pusher {
            public static PusherOptions Options = new PusherOptions() {
                Encrypted = true,
                Host = "notifier.bitskins.com"
            };
        }

    }
}
