using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SteamBot.MarketBot.CS
{
    public class Experiment
    {
        private DateTime begin;
        private DateTime end;
        public Experiment(DateTime begin, DateTime end)
        {
            this.begin = begin;
            this.end = end;
        }

        public bool IsRunning()
        {
            DateTime temp = DateTime.Now;
            return temp > begin && temp < end;
        }
    }

    public class NewBuyFormula : Experiment
    {
        public double WantToBuy { get; set; }

        public NewBuyFormula(DateTime begin, DateTime end) : base(begin, end)
        {
        }

        public NewBuyFormula(DateTime begin, DateTime end, double wantToBuy)
            : this(begin, end)
        {
            WantToBuy = wantToBuy;
        }

    }
}
