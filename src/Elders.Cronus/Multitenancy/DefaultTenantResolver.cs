﻿using System;
using System.Linq;
using Elders.Cronus.EventStore;
using Elders.Cronus.Projections;

namespace Elders.Cronus.Multitenancy
{
    public class DefaultTenantResolver :
        ITenantResolver<string>,
        ITenantResolver<IAggregateRootId>,
        ITenantResolver<AggregateCommit>,
        ITenantResolver<ProjectionCommit>,
        ITenantResolver<IMessage>,
        ITenantResolver<IBlobId>,
        ITenantResolver<CronusMessage>
    {
        public string Resolve(ProjectionCommit projectionCommit)
        {
            if (ReferenceEquals(null, projectionCommit) == true) throw new ArgumentNullException(nameof(projectionCommit));

            string tenant;
            if (TryResolve(projectionCommit.ProjectionId.RawId, out tenant))
                return tenant;

            throw new NotSupportedException($"Unable to resolve tenant for id {projectionCommit.ProjectionId}");
        }

        public string Resolve(IBlobId id)
        {
            if (ReferenceEquals(null, id) == true) throw new ArgumentNullException(nameof(id));

            string tenant;
            if (TryResolve(id.RawId, out tenant))
                return tenant;

            throw new NotSupportedException($"Unable to resolve tenant for id {id}");
        }

        public string Resolve(IAggregateRootId id)
        {
            if (ReferenceEquals(null, id) == true) throw new ArgumentNullException(nameof(id));

            if (id is AggregateRootId)
                return ((AggregateRootId)id).Tenant;

            throw new NotSupportedException($"Unable to resolve tenant for id {id}");
        }

        public string Resolve(AggregateCommit aggregateCommit)
        {
            if (ReferenceEquals(null, aggregateCommit) == true) throw new ArgumentNullException(nameof(aggregateCommit));

            string tenant;
            if (TryResolve(aggregateCommit.AggregateRootId, out tenant))
                return tenant;

            throw new NotSupportedException($"Unable to resolve tenant for id {aggregateCommit.AggregateRootId}");
        }

        public string Resolve(IMessage message)
        {
            var tenantPropertyMeta = message.GetType().GetProperty("Tenant", typeof(string));
            if (tenantPropertyMeta is null == false)
            {
                return (string)tenantPropertyMeta.GetValue(message);
            }

            var idMeta = message.GetType().GetProperties().Where(p => typeof(IBlobId).IsAssignableFrom(p.PropertyType)).FirstOrDefault();
            IBlobId id = idMeta?.GetValue(message) as IBlobId;
            if (id is null == false)
            {
                return Resolve(id);
            }

            throw new NotSupportedException($"Unable to resolve tenant from {message}");
        }

        public string Resolve(CronusMessage cronusMessage)
        {
            var tenant = cronusMessage.GetTenant();
            if (string.IsNullOrEmpty(tenant))
            {
                return Resolve(cronusMessage.Payload);
            }

            return tenant;
        }

        public string Resolve(string source)
        {
            return source;
        }

        bool TryResolve(byte[] id, out string tenant)
        {
            tenant = string.Empty;
            var urn = System.Text.Encoding.UTF8.GetString(id);
            AggregateUrn aggregateUrn;

            if (AggregateUrn.TryParse(urn, out aggregateUrn))
            {
                tenant = aggregateUrn.Tenant;
                return true;
            }

            return false;
        }
    }
}
