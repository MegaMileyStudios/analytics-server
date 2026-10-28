// Compiled by build-fixtures.sh into tiny symbol files for the symbolication tests.
// The functions are named like NativeAOT names C# methods (<Assembly>_<Namespace>_<Type>__<Method>),
// so they stand in for both a game's native frames and its C# frames.

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

__attribute__((noinline)) int Game_Game_Main__Crash(int value)
{
    volatile int *pointer = 0;
    return *pointer + value; // crash-line
}

__attribute__((noinline)) int Game_Game_Main__Run(int value)
{
    int result = Game_Game_Main__Crash(value); // call-line
    return result + 1;
}

EXPORT int godotsharp_game_main_init(void)
{
    return Game_Game_Main__Run(1);
}
