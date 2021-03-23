using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace DrawControl3dWrapper
{
    /// <summary>
    /// 
//
    /// </summary>
    [ComVisible(true),Guid("16a282fa-f2e9-4e01-8642-122497b14f6e")
        //,InterfaceType(ComInterfaceType.InterfaceIsDual)
        ]
    public interface IControlWrapper
    {
        void LoadAnyModel(string modelFileName);
        int SelectTheSame(string guid);
        
        double Width { get; set; }
        double Height { get; set; }
    }
}
