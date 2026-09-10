// Battalion: Fulda
// D0 toolchain verification program.
//
// This deliberately uses only the C64 KERNAL CHROUT routine. It clears the
// screen, prints three status lines, and then remains in a harmless loop.

BasicUpstart2(start)              // Add a BASIC SYS launcher at $0801.

* = $0810                        // Keep the machine-code entry point explicit.

start:
    lda #$93                     // PETSCII CLEAR/HOME control code.
    jsr $ffd2                    // KERNAL CHROUT: clear the screen.

    ldx #0
print_next:
    lda message,x
    beq finished                 // A zero byte terminates the message.
    jsr $ffd2                    // Print the current PETSCII character.
    inx
    bne print_next               // The message is shorter than 256 bytes.

finished:
    jmp finished                 // Keep the completed screen visible.

.encoding "petscii_upper"
message:
    .text "BATTALION: FULDA"
    .byte 13                     // Carriage return starts the next line.
    .text "D0 TOOLCHAIN TEST"
    .byte 13
    .text "BUILD SUCCESSFUL"
    .byte 0
