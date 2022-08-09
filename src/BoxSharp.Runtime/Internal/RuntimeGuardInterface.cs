using System.Runtime.CompilerServices;

namespace BoxSharp.Runtime.Internal
{
    /// <summary>
    /// Code compiled using BoxCompiler will call these methods.
    /// </summary>
    public static class RuntimeGuardInterface
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnterMethod(int gid)
        {
            RuntimeGuardInstances.GetFast(gid).GuardEnter();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void EnterStaticConstructor(int gid)
        {
            RuntimeGuardInstances.GetFast(gid).GuardEnterStaticConstructor();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ExitStaticConstructor(int gid)
        {
            RuntimeGuardInstances.GetFast(gid).GuardExitStaticConstructor();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T AfterNewObject<T>(int gid, T obj)
        {
            RuntimeGuardInstances.GetFast(gid).GuardCount(1);
            return obj;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T[] AfterNewArray<T>(int gid, T[] arr)
        {
            RuntimeGuardInstances.GetFast(gid).GuardCount(arr.Length);
            return arr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void BeforeJump(int gid)
        {
            RuntimeGuardInstances.GetFast(gid).GuardJump();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T BeforeAwait<T>(int _, T value)
        {
            // TODO
            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T AfterAwait<T>(int _, T value)
        {
            // TODO
            return value;
        }
    }
}
