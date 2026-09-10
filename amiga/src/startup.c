extern int game_main(void);

__attribute__((optimize("no-tree-loop-distribute-patterns")))
void *memcpy(void *destination, const void *source, unsigned long length)
{
    unsigned char *to = (unsigned char *)destination;
    const unsigned char *from = (const unsigned char *)source;
    while (length--) *to++ = *from++;
    return destination;
}

/* Minimal Amiga executable entry point. The operating system supplies the
 * process and stack; game_main owns and releases every library it opens. */
__attribute__((used, section(".text.startup")))
void _start(void)
{
    volatile int result = game_main();
    (void)result;
    __asm__ volatile ("" ::: "memory");
}
