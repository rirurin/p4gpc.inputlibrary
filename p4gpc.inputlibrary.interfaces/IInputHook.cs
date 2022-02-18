using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace p4gpc.inputlibrary.interfaces
{
    public interface IInputHook
    {
        event OnInputEvent OnInput;
    }
    public delegate void OnInputEvent(int input, bool risingEdge, bool controlType);
}