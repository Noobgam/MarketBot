using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PusherClient;

namespace BitSkins {
    public class PusherSocket {
        private Pusher pusher;
        bool connected = false;
        private void ConnectionStateChanged(object sender, ConnectionState state) {
            Console.WriteLine("Connection state: " + state.ToString());
            SubscribeListed(OnMessageStub);
        }

        private void OnMessageStub(dynamic message) {
            message = message;
        }

        static void Error(object sender, PusherException ex) {
            Console.WriteLine(ex.ToString());
        }

        public PusherSocket() {
            pusher = new Pusher("c0eef4118084f8164bec65e6253bf195", Consts.Pusher.Options);
            pusher.ConnectionStateChanged += ConnectionStateChanged;

            pusher.Connect();
        }

        public void SubscribeListed(Action<dynamic> callback) {
            pusher.Subscribe("inventory_changes");
            pusher.Bind("listed", callback);
        }
    }
}
