using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EES_MotorOverload_V1
{
    public class BearingModel
    {
        public int? ID { get; set; }
        public string BearNB { get; set; }
        public string NB { get; set; }
        public string BD { get; set; }
        public string PD { get; set; }
        public string PHI { get; set; }
        public string BPFO { get; set; }
        public string BPFI { get; set; }
        public string FTF { get; set; }
        public string BSF { get; set; }

        public override string ToString()
        {
            return BearNB ?? $"Bearing #{ID}";
        }
    }
}
