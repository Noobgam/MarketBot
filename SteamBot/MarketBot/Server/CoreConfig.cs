using Newtonsoft.Json;
using SteamBot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SteamBot.Configuration;

namespace Server {

    [Serializable]
    class CoreConfig : Configuration {
        public new BotConfig[] Bots { get; set; }
    }

    [Serializable]
    class BotConfig : BotInfo, IEquatable<BotConfig> {

        public double Weight;

        public bool Force;

        public override bool Equals(object obj) {
            return Equals(obj as BotConfig);
        }

        public bool Equals(BotConfig other) {
            return other != null &&
                   base.Equals(other) &&
                   Weight == other.Weight &&
                   Force == other.Force;
        }

        public override int GetHashCode() {
            var hashCode = -2117736981;
            hashCode = hashCode * -1521134295 + base.GetHashCode();
            hashCode = hashCode * -1521134295 + Weight.GetHashCode();
            hashCode = hashCode * -1521134295 + Force.GetHashCode();
            return hashCode;
        }

        public static bool operator ==(BotConfig config1, BotConfig config2) {
            return EqualityComparer<BotConfig>.Default.Equals(config1, config2);
        }

        public static bool operator !=(BotConfig config1, BotConfig config2) {
            return !(config1 == config2);
        }
    }
}
