using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Middleware_console
{
    #region Data Models
    public class ScadaScreenModel
    {
        public string ScreenName { get; set; }
        public List<ScadaLayerModel> Layers { get; set; }
        public List<ScadaItemModel> Items { get; set; }
    }
    public class ScadaLayerModel
    {
        public string LayerName { get; set; }
        public List<ScadaItemModel> Items { get; set; }
    }
    public class ScadaItemModel
    {
        public string Type { get; set; }
        public string Name { get; set; }
        public bool? EnableCreation { get; set; } = true;
        public Dictionary<string, object> Properties { get; set; }
        public Dictionary<string, string> Events { get; set; }
        public List<ScadaItemModel> Items { get; set; }
        public string TagName { get; set; }
    }
#endregion
}
