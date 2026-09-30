PROGRAM P1
VAR RETAIN
    Local1 : DINT;
END_VAR

Main();

ACTION Main
    Counter := Counter + 1;
END_ACTION

ACTION Fault
    Local1 := 0;
END_ACTION

ACTION Prescan (* hook: init *)
END_ACTION
END_PROGRAM

PROGRAM P2

Main();

ACTION Main
    Counter := 0;
END_ACTION

ACTION Prescan (* hook: init *)
END_ACTION
END_PROGRAM

PROGRAM P3

Main();

ACTION Main
    Counter := Counter - 1;
END_ACTION

ACTION Prescan (* hook: init *)
END_ACTION
END_PROGRAM

PROGRAM P4

Main();

ACTION Main
    Counter := Cons;
END_ACTION

ACTION Prescan (* hook: init *)
END_ACTION
END_PROGRAM

PROGRAM Folder


ACTION Prescan (* hook: init *)
END_ACTION
END_PROGRAM

