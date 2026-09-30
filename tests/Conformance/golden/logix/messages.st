PROGRAM P
VAR RETAIN
    State : DINT;
END_VAR

Main();

ACTION Main
    VAR_TEMP
        _rc1 : BOOL;
        _rc2 : BOOL;
        _rc3 : BOOL;
        _rc4 : BOOL;
        _rc5 : BOOL;
    END_VAR
    (* EN := Trigger AND NOT Read_Remote.EN *) MSG(Read_Remote); (* logix *)
    _rc1 := State = 0;
    IF _rc1 THEN
        MsgData.Create_Source.Type := 1;
    END_IF;
    IF _rc1 THEN
        MsgData.Create_Source.Addr.Family := TO_INT#wrap(2);
    END_IF;
    IF _rc1 THEN
        MsgData.Create_Source.Addr.Port := TO_INT#wrap(5000);
    END_IF;
    (* EN := _rc1 *) MSG(SKT_Create); (* logix *)
    IF SKT_Create.DN THEN
        SKT_Open.Instance := MsgData.Instance;
    END_IF;
    IF SKT_Create.DN THEN
        SKT_Write.Instance := MsgData.Instance;
    END_IF;
    IF SKT_Create.DN THEN
        SKT_Read.Instance := MsgData.Instance;
    END_IF;
    IF SKT_Create.DN THEN
        SKT_Delete.Instance := MsgData.Instance;
    END_IF;
    IF SKT_Create.DN THEN
        State := 1;
    END_IF;
    _rc2 := State = 1;
    (* EN := _rc2 *) MSG(SKT_Open); (* logix *)
    IF State = 1 AND SKT_Open.DN THEN
        State := 2;
    END_IF;
    _rc3 := State = 2;
    (* EN := _rc3 *) MSG(SKT_Write); (* logix *)
    IF State = 2 AND SKT_Write.DN THEN
        State := 3;
    END_IF;
    _rc4 := State = 3;
    (* EN := _rc4 *) MSG(SKT_Read); (* logix *)
    IF State = 3 AND SKT_Read.DN THEN
        State := 4;
    END_IF;
    _rc5 := State = 4;
    (* EN := _rc5 *) MSG(SKT_Delete); (* logix *)
    IF State = 4 AND SKT_Delete.DN THEN
        State := 0;
    END_IF;
    (* EN := Trigger *) MSG(Get_Identity); (* logix *)
    IF Trigger THEN
        ProdCount := ConsCount;
    END_IF;
END_ACTION

ACTION Prescan (* hook: init *)
    MSG(Read_Remote); (* logix *)
    MSG(SKT_Create); (* logix *)
    MSG(SKT_Open); (* logix *)
    MSG(SKT_Write); (* logix *)
    MSG(SKT_Read); (* logix *)
    MSG(SKT_Delete); (* logix *)
    MSG(Get_Identity); (* logix *)
END_ACTION
END_PROGRAM

