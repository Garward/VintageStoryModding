using System;
using System.Reflection;
using System.Reflection.Emit;

namespace VRPG.Modules.Dungeons;

internal static class ManifoldWorldgenProxyFactory
{
    private static readonly ModuleBuilder ModuleBuilder = AssemblyBuilder
        .DefineDynamicAssembly(new AssemblyName("VRPG.ManifoldDynamicProxies"), AssemblyBuilderAccess.Run)
        .DefineDynamicModule("VRPG.ManifoldDynamicProxies");

    private static int counter;

    public static object Create(Type worldgenStrategyInterface, DungeonWorldgenRuntime runtime)
    {
        if (!worldgenStrategyInterface.IsInterface)
        {
            throw new ArgumentException("Worldgen strategy type must be an interface.", nameof(worldgenStrategyInterface));
        }

        Type proxyType = BuildProxyType(worldgenStrategyInterface);
        return Activator.CreateInstance(proxyType, runtime)!;
    }

    private static Type BuildProxyType(Type worldgenStrategyInterface)
    {
        string typeName = "VRPG.ManifoldDynamicProxy" + counter++;
        TypeBuilder type = ModuleBuilder.DefineType(
            typeName,
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class);

        type.AddInterfaceImplementation(worldgenStrategyInterface);

        FieldBuilder runtimeField = type.DefineField(
            "runtime",
            typeof(DungeonWorldgenRuntime),
            FieldAttributes.Private | FieldAttributes.InitOnly);

        ConstructorBuilder ctor = type.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            new[] { typeof(DungeonWorldgenRuntime) });
        ILGenerator ctorIl = ctor.GetILGenerator();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg_1);
        ctorIl.Emit(OpCodes.Stfld, runtimeField);
        ctorIl.Emit(OpCodes.Ret);

        ImplementForwarder(type, runtimeField, worldgenStrategyInterface, "OnInitialize");
        ImplementForwarder(type, runtimeField, worldgenStrategyInterface, "GenerateColumn");

        return type.CreateType()!;
    }

    private static void ImplementForwarder(
        TypeBuilder type,
        FieldBuilder runtimeField,
        Type worldgenStrategyInterface,
        string methodName)
    {
        MethodInfo interfaceMethod = worldgenStrategyInterface.GetMethod(methodName)
            ?? throw new MissingMethodException(worldgenStrategyInterface.FullName, methodName);
        ParameterInfo[] parameters = interfaceMethod.GetParameters();
        if (parameters.Length != 1)
        {
            throw new InvalidOperationException("Unexpected Manifold IWorldgenStrategy method shape.");
        }

        MethodInfo runtimeMethod = typeof(DungeonWorldgenRuntime).GetMethod(methodName)
            ?? throw new MissingMethodException(typeof(DungeonWorldgenRuntime).FullName, methodName);

        MethodBuilder method = type.DefineMethod(
            methodName,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
            typeof(void),
            new[] { parameters[0].ParameterType });

        ILGenerator il = method.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, runtimeField);
        il.Emit(OpCodes.Ldarg_1);
        if (parameters[0].ParameterType.IsValueType)
        {
            il.Emit(OpCodes.Box, parameters[0].ParameterType);
        }
        il.Emit(OpCodes.Callvirt, runtimeMethod);
        il.Emit(OpCodes.Ret);

        type.DefineMethodOverride(method, interfaceMethod);
    }
}
