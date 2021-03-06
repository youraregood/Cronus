﻿using Elders.Cronus.EventStore;
using Elders.Cronus.EventStore.Index;
using System.Collections.Generic;

namespace Elders.Cronus.Discoveries
{
    public abstract class EventStoreDiscoveryBase : DiscoveryBase<IEventStore>
    {
        protected override DiscoveryResult<IEventStore> DiscoverFromAssemblies(DiscoveryContext context)
        {
            return new DiscoveryResult<IEventStore>(GetModels(context));
        }

        IEnumerable<DiscoveredModel> GetModels(DiscoveryContext context)
        {
            var loadedTypes = context.Assemblies.Find<IEventStoreIndex>();
            yield return new DiscoveredModel(typeof(TypeContainer<IEventStoreIndex>), new TypeContainer<IEventStoreIndex>(loadedTypes));
        }
    }
}
