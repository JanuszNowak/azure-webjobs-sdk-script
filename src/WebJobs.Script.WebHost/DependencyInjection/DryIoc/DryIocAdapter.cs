﻿// <autogenerated/>
// Auto-generated comment added to avoid code analisys warnings

/*
The MIT License (MIT)

Copyright (c) 2016 Maksim Volkau

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ImTools;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace DryIoc.Microsoft.DependencyInjection
{
    /// <summary>Adapts DryIoc container to be used as MS.DI service provider, plus provides the helpers
    /// to simplify work with adapted container.</summary>
    public static class DryIocAdapter
    {
        /// <summary>Adapts passed <paramref name="container"/> to Microsoft.DependencyInjection conventions,
        /// registers DryIoc implementations of <see cref="IServiceProvider"/> and <see cref="IServiceScopeFactory"/>,
        /// and returns NEW container.
        /// </summary>
        /// <param name="container">Source container to adapt.</param>
        /// <param name="descriptors">(optional) Specify service descriptors or use <see cref="Populate"/> later.</param>
        /// <param name="registerDescriptor">(optional) Custom registration action, should return true to skip normal registration.</param>
        /// <param name="throwIfUnresolved">(optional) Instructs DryIoc to throw exception
        /// for unresolved type instead of fallback to default Resolver.</param>
        /// <returns>New container adapted to AspNetCore DI conventions.</returns>
        /// <example>
        /// <code><![CDATA[
        ///     var container = new Container();
        ///     // you may start to register you services here
        ///
        ///     var adaptedContainer = container.WithDependencyInjectionAdapter(services);
        ///     var serviceProvider = adaptedContainer.Resolve<IServiceProvider>();
        ///
        ///     // to register service per Request use Reuse.InCurrentScope
        ///     adaptedContainer.Register<IMyService, MyService>(Reuse.InCurrentScope)
        ///]]></code>
        /// </example>
        /// <remarks>You still need to Dispose adapted container at the end / application shutdown.</remarks>
        public static IContainer WithDependencyInjectionAdapter(this IContainer container,
            IEnumerable<ServiceDescriptor> descriptors = null,
            Func<IRegistrator, ServiceDescriptor, bool> registerDescriptor = null,
            Func<Type, bool> throwIfUnresolved = null)
        {
            if (container.ScopeContext != null)
                throw new ArgumentException(
                    $"Container with ambient scope context is not supported by MS.DI: ${container}");

            var adapter = container.With(rules => rules
                .With(FactoryMethod.ConstructorWithResolvableArguments)
                .WithFactorySelector(Rules.SelectLastRegisteredFactory())
                .WithTrackingDisposableTransients());

            adapter.RegisterMany<DryIocServiceProvider>(
                setup: Setup.With(useParentReuse: true),
                made: Parameters.Of.Type(_ => throwIfUnresolved));

            adapter.Register<IServiceScopeFactory, DryIocServiceScopeFactory>(Reuse.ScopedOrSingleton);

            // Registers service collection
            if (descriptors != null)
                adapter.Populate(descriptors, registerDescriptor);

            return adapter;
        }

        /// <summary>Facade to consolidate DryIoc registrations in <typeparamref name="TCompositionRoot"/></summary>
        /// <typeparam name="TCompositionRoot">The class will be created by container on Startup
        /// to enable registrations with injected <see cref="IRegistrator"/> or full <see cref="IContainer"/>.</typeparam>
        /// <param name="container">Adapted container</param> <returns>Service provider</returns>
        /// <example>
        /// <code><![CDATA[
        /// public class ExampleCompositionRoot
        /// {
        ///    // if you need the whole container then change parameter type from IRegistrator to IContainer
        ///    public ExampleCompositionRoot(IRegistrator r)
        ///    {
        ///        r.Register<ISingletonService, SingletonService>(Reuse.Singleton);
        ///        r.Register<ITransientService, TransientService>(Reuse.Transient);
        ///        r.Register<IScopedService, ScopedService>(Reuse.InCurrentScope);
        ///    }
        /// }
        /// ]]></code>
        /// </example>
        public static IServiceProvider ConfigureServiceProvider<TCompositionRoot>(this IContainer container)
        {
            container.Register<TCompositionRoot>();
            container.Resolve<TCompositionRoot>();
            return container.Resolve<IServiceProvider>();
        }

        /// <summary>Registers service descriptors into container. May be called multiple times with different service collections.</summary>
        /// <param name="container">The container.</param>
        /// <param name="descriptors">The service descriptors.</param>
        /// <param name="registerDescriptor">(optional) Custom registration action, should return true to skip normal registration.</param>
        /// <example>
        /// <code><![CDATA[
        ///     // example of normal descriptor registration together with factory method registration for SomeService.
        ///     container.Populate(services, (r, service) => {
        ///         if (service.ServiceType == typeof(SomeService)) {
        ///             r.Register<SomeService>(Made.Of(() => CreateCustomService()), Reuse.Singleton);
        ///             return true;
        ///         };
        ///         return false; // fallback to normal registrations for the rest of the descriptors.
        ///     });
        /// ]]></code>
        /// </example>
        public static void Populate(this IContainer container, IEnumerable<ServiceDescriptor> descriptors,
            Func<IRegistrator, ServiceDescriptor, bool> registerDescriptor = null, IReuse singletonReuse = null)
        {
            foreach (var descriptor in descriptors)
                if (registerDescriptor == null || !registerDescriptor(container, descriptor))
                    container.RegisterDescriptor(descriptor, singletonReuse);
        }

        /// <summary>Uses passed descriptor to register service in container:
        /// maps DI Lifetime to DryIoc Reuse,
        /// and DI registration type to corresponding DryIoc Register, RegisterDelegate or RegisterInstance.</summary>
        /// <param name="container">The container.</param>
        /// <param name="descriptor">Service descriptor.</param>
        public static void RegisterDescriptor(this IContainer container, ServiceDescriptor descriptor, IReuse singletonReuse = null)
        {
            var reuse = ConvertLifetimeToReuse(descriptor.Lifetime, singletonReuse);

            if (descriptor.ImplementationType != null)
            {
                container.Register(descriptor.ServiceType, descriptor.ImplementationType, reuse);
            }
            else if (descriptor.ImplementationFactory != null)
            {
                if (descriptor.Lifetime == ServiceLifetime.Singleton)
                {
                    container.RegisterDelegate(descriptor.ServiceType,
                        r =>
                        {
                            // Use the Root (or self, if already at Root) to resolve this at the Singleton scope.
                            return descriptor.ImplementationFactory(r.RootOrSelf().Resolve<IServiceProvider>());
                        },
                        reuse);
                }
                else
                {
                    container.RegisterDelegate(descriptor.ServiceType,
                        r => descriptor.ImplementationFactory(r.Resolve<IServiceProvider>()),
                        reuse);
                }
            }
            else
            {
                container.UseInstance(descriptor.ServiceType, descriptor.ImplementationInstance,
                    IfAlreadyRegistered.AppendNotKeyed);
            }
        }

        private static IReuse ConvertLifetimeToReuse(ServiceLifetime lifetime, IReuse singletonReuse = null)
        {
            switch (lifetime)
            {
                case ServiceLifetime.Singleton:
                    return singletonReuse ?? Reuse.Singleton;
                case ServiceLifetime.Scoped:
                    return Reuse.ScopedOrSingleton; // note: because the infrastructure services may be resolved w/out scope
                case ServiceLifetime.Transient:
                    return Reuse.Transient;
                default:
                    throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Not supported lifetime");
            }
        }
    }

    /// <summary>Wraps DryIoc scoped container.</summary>
    public sealed class DryIocServiceProvider : IServiceProvider, ISupportRequiredService, IServiceScope
    {
        private readonly IResolverContext _scopedResolver;
        private readonly Func<Type, bool> _throwIfUnresolved;

        /// <summary>Uses passed container for scoped resolutions.</summary>
        /// <param name="scopedResolver">A scoped resolver context to wrap</param>
        /// <param name="throwIfUnresolved">(optional) Instructs DryIoc to throw exception
        /// for unresolved type instead of fallback to default Resolver.</param>
        public DryIocServiceProvider(IResolverContext scopedResolver, Func<Type, bool> throwIfUnresolved)
        {
            _scopedResolver = scopedResolver;
            _throwIfUnresolved = throwIfUnresolved;
        }

        /// <summary>Just for convenience and access from tests</summary>
        public IServiceProvider ServiceProvider => this;

        /// <summary>Delegates resolution to scoped container. In case the service is unresolved
        /// depending on provided policy it will either fallback to default DI resolver,
        /// or will throw the original DryIoc exception (cause it good to know the reason).</summary>
        /// <param name="serviceType">Service type to resolve.</param>
        /// <returns>Resolved service object.</returns>
        public object GetService(Type serviceType) =>
            _scopedResolver.Resolve(serviceType, _throwIfUnresolved == null || !_throwIfUnresolved(serviceType));

        /// <summary> Gets service of type <paramref name="serviceType" /> from the <see cref="T:System.IServiceProvider" /> implementing
        /// this interface. </summary>
        /// <param name="serviceType">An object that specifies the type of service object to get.</param>
        /// <returns>A service object of type <paramref name="serviceType" />.
        /// Throws an exception if the <see cref="T:System.IServiceProvider" /> cannot create the object.</returns>
        public object GetRequiredService(Type serviceType) => _scopedResolver.Resolve(serviceType);

        /// <summary>Disposes underlying container.</summary>
        public void Dispose() => _scopedResolver.Dispose();
    }

    /// <summary>Creates/opens new scope in passed scoped container.</summary>
    public sealed class DryIocServiceScopeFactory : IServiceScopeFactory
    {
        /// <summary>Stores passed scoped container to open nested scope.</summary>
        /// <param name="scopedResolver">Scoped container to be used to create nested scope.</param>
        public DryIocServiceScopeFactory(IResolverContext scopedResolver)
        {
            _scopedResolver = scopedResolver;
        }

        /// <summary>Opens scope and wraps it into DI <see cref="IServiceScope"/> interface.</summary>
        /// <returns>DI wrapper of opened scope.</returns>
        public IServiceScope CreateScope() => _scopedResolver.OpenScope().Resolve<IServiceScope>();

        private readonly IResolverContext _scopedResolver;
    }
}