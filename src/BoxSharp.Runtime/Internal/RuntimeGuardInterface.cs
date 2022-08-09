using System.Runtime.CompilerServices;

namespace BoxSharp.Runtime.Internal
{
    /// <summary>
    /// Code compiled using BoxCompiler will call these methods.
    /// </summary>
    public static class RuntimeGuardInterface
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RuntimeGuard InitializeStaticField(int gid)
        {
            return RuntimeGuardInstances.Get(gid);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnterMethod(RuntimeGuard guard)
        {
            guard.GuardEnter();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnterStaticConstructor(RuntimeGuard guard)
        {
            guard.GuardEnterStaticConstructor();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ExitStaticConstructor(RuntimeGuard guard)
        {
            guard.GuardExitStaticConstructor();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T AfterNewObject<T>(RuntimeGuard guard, T obj)
        {
            guard.GuardCount(1);
            return obj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] AfterNewArray<T>(RuntimeGuard guard, T[] arr)
        {
            guard.GuardCount(arr.Length);
            return arr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BeforeJump(RuntimeGuard guard)
        {
            guard.GuardJump();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T BeforeAwait<T>(RuntimeGuard _, T value)
        {
            // TODO
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T AfterAwait<T>(RuntimeGuard _, T value)
        {
            // TODO
            return value;
        }
    }
}
