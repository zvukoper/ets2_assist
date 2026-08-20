using System.Collections.Generic;

namespace ETS2_Assist_GUI
{
    public class CityData
    {
        public double X { get; set; }
        public double Z { get; set; }
        public string Name { get; set; } = "";
    }

    public class RoadSegment
    {
        public double X1 { get; set; }
        public double Z1 { get; set; }
        public double X2 { get; set; }
        public double Z2 { get; set; }
        public string RoadType { get; set; } = "default";
    }
}