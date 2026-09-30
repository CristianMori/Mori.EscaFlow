PROGRAM P
VAR RETAIN
    Step_000 : SFC_STEP;
    Step_001 : SFC_STEP;
    Step_002 : SFC_STEP;
    Step_003 : SFC_STEP;
    Step_004 : SFC_STEP;
    Tran_000 : BOOL;
    Tran_001 : BOOL;
    Tran_002 : BOOL;
    Tran_003 : BOOL;
    Action_000 : SFC_ACTION;
    Action_001 : SFC_ACTION;
    Action_002 : SFC_ACTION;
    Action_003 : SFC_ACTION;
    Action_004 : SFC_ACTION;
    Action_005 : SFC_ACTION;
    Action_006 : SFC_ACTION;
    Action_007 : SFC_ACTION;
    Action_008 : SFC_ACTION;
    Stop_000 : SFC_STOP;
END_VAR

Main();

ACTION Main
END_ACTION

ACTION Prescan (* hook: init *)
END_ACTION
END_PROGRAM

