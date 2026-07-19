using System.Collections.Generic;

namespace WS_Modules
{
    internal interface IEventSearchService
    {
        Dictionary<string, EventSystemInfo> SearchEventSystems();
    }
}

