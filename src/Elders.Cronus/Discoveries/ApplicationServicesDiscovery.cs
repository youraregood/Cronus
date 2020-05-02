﻿using System.Collections.Generic;
using System.Linq;
using Elders.Cronus.MessageProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace Elders.Cronus.Discoveries
{
    public abstract class HandlersDiscovery<T> : DiscoveryBase<T>
    {
        protected override DiscoveryResult<T> DiscoverFromAssemblies(DiscoveryContext context)
        {
            return new DiscoveryResult<T>(GetModels(context));
        }

        protected virtual IEnumerable<DiscoveredModel> GetModels(DiscoveryContext context)
        {
            var foundTypes = context.FindService<T>();
            foreach (var type in foundTypes)
            {
                yield return new DiscoveredModel(type, type, ServiceLifetime.Transient);
            }

            yield return new DiscoveredModel(typeof(TypeContainer<T>), new TypeContainer<T>(foundTypes));
            yield return new DiscoveredModel(typeof(IHandlerFactory), provider => new DefaultHandlerFactory(type => provider.GetRequiredService(type)), ServiceLifetime.Transient);
        }
    }

    public class ApplicationServicesDiscovery : HandlersDiscovery<IApplicationService> { }

    public class ProjectionsDiscovery : HandlersDiscovery<IProjection>
    {
        protected override IEnumerable<DiscoveredModel> GetModels(DiscoveryContext context)
        {
            var models = base.GetModels(context).ToList();
            models.Add(new DiscoveredModel(typeof(Projections.Versioning.ProjectionHasher), typeof(Projections.Versioning.ProjectionHasher), ServiceLifetime.Singleton));

            return models;
        }
    }

    public class PortsDiscovery : HandlersDiscovery<IPort> { }

    public class SagasDiscovery : HandlersDiscovery<ISaga> { }

    public class GatewaysDiscovery : HandlersDiscovery<IGateway> { }

    public class TriggersDiscovery : HandlersDiscovery<ITrigger> { }
}
