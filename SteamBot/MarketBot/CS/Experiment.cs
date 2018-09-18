using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamBot.MarketBot.CS {
    public class Experiment {
        private DateTime begin;
        private DateTime end;
        public Experiment(DateTime begin, DateTime end) {
            this.begin = begin;
            this.end = end;
        }

        public bool IsRunning() {
            DateTime temp = DateTime.Now;
            return temp > begin && temp < end;
        }
    }

    public class NewBuyFormula : Experiment {
        public double WantToBuy { get; set; }

        public NewBuyFormula(DateTime begin, DateTime end) : base(begin, end) {
        }

        public NewBuyFormula(DateTime begin, DateTime end, double wantToBuy)
            : this(begin, end) {
            WantToBuy = wantToBuy;
        }

        public override bool Equals(object obj) {
            var formula = obj as NewBuyFormula;
            return formula != null &&
                   WantToBuy == formula.WantToBuy;
        }

        public static bool operator ==(NewBuyFormula formula1, NewBuyFormula formula2) {
            return EqualityComparer<NewBuyFormula>.Default.Equals(formula1, formula2);
        }

        public static bool operator !=(NewBuyFormula formula1, NewBuyFormula formula2) {
            return !(formula1 == formula2);
        }
    }
    public class SellMultiplier : Experiment {
        public double Multiplier { get; set; }

        public SellMultiplier(DateTime begin, DateTime end) : base(begin, end) {
        }

        public SellMultiplier(DateTime begin, DateTime end, double sellMultiplier)
            : this(begin, end) {
            Multiplier = sellMultiplier;
        }

        public override bool Equals(object obj) {
            var multiplier = obj as SellMultiplier;
            return multiplier != null &&
                   Multiplier == multiplier.Multiplier;
        }

        public static bool operator ==(SellMultiplier multiplier1, SellMultiplier multiplier2) {
            return EqualityComparer<SellMultiplier>.Default.Equals(multiplier1, multiplier2);
        }

        public static bool operator !=(SellMultiplier multiplier1, SellMultiplier multiplier2) {
            return !(multiplier1 == multiplier2);
        }
    }

}
