using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MarketBot.Server {
    [Serializable]
    struct CoreConfig {
        [JsonProperty("bots")]
        public readonly List<BotConfig> botList;
    }

    [Serializable]
    struct BotConfig : IEquatable<BotConfig> {
        [JsonProperty("name")]
        public readonly string Name;

        [JsonProperty("weight")]
        public readonly double Weight;

        [JsonProperty("force")]
        public readonly bool Force;

        public override bool Equals(object obj) {
            return obj is BotConfig && Equals((BotConfig)obj);
        }

        public bool Equals(BotConfig other) {
            return Name == other.Name &&
                   Weight == other.Weight;
        }

        public override int GetHashCode() {
            var hashCode = -1185841457;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(Name);
            hashCode = hashCode * -1521134295 + Weight.GetHashCode();
            return hashCode;
        }

        public static bool operator ==(BotConfig config1, BotConfig config2) {
            return config1.Equals(config2);
        }

        public static bool operator !=(BotConfig config1, BotConfig config2) {
            return !(config1 == config2);
        }
    }
}
