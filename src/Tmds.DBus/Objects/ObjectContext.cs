using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tmds.DBus.Objects
{
    /// <summary>
    /// Inherit from this to get access to per-call context
    /// </summary>
    public class ObjectContext
    {
        protected ICallContext CallContext { get; private set; }

        /// <summary>
        /// Checks if globally registered object should be exposed at given path
        /// </summary>
        /// <returns>If object should be exposed</returns>
        public virtual bool CheckExposure()
        {
            return true;
        }

        internal void SetContext(ICallContext ctx) { CallContext = ctx; }
    }
}
