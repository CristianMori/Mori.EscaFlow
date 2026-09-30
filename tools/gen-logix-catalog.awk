BEGIN {
  # parameter names for the common instructions (1756-RM003 operand names); others get argN
  names["XIC"]="dataBit"; names["XIO"]="dataBit"; names["OTE"]="dataBit"; names["OTL"]="dataBit"; names["OTU"]="dataBit"; names["ONS"]="storageBit";
  names["OSR"]="storageBit,outputBit"; names["OSF"]="storageBit,outputBit";
  names["TON"]="timer,preset,accum"; names["TOF"]="timer,preset,accum"; names["RTO"]="timer,preset,accum";
  names["CTU"]="counter,preset,accum"; names["CTD"]="counter,preset,accum"; names["RES"]="structure";
  names["CMP"]="expression"; names["EQU"]="sourceA,sourceB"; names["GEQ"]="sourceA,sourceB"; names["GRT"]="sourceA,sourceB"; names["LEQ"]="sourceA,sourceB"; names["LES"]="sourceA,sourceB"; names["NEQ"]="sourceA,sourceB";
  names["LIM"]="lowLimit,test,highLimit"; names["MEQ"]="source,mask,compare";
  names["CPT"]="dest,expression"; names["ADD"]="sourceA,sourceB,dest"; names["SUB"]="sourceA,sourceB,dest"; names["MUL"]="sourceA,sourceB,dest"; names["DIV"]="sourceA,sourceB,dest"; names["MOD"]="sourceA,sourceB,dest";
  names["SQR"]="source,dest"; names["NEG"]="source,dest"; names["ABS"]="source,dest";
  names["MOV"]="source,dest"; names["MVM"]="source,mask,dest"; names["BTD"]="source,sourceBit,dest,destBit,length"; names["BTDT"]="source,sourceBit,dest,destBit,length,target"; names["CLR"]="dest"; names["SWPB"]="source,orderMode,dest";
  names["AND"]="sourceA,sourceB,dest"; names["OR"]="sourceA,sourceB,dest"; names["XOR"]="sourceA,sourceB,dest"; names["NOT"]="source,dest";
  names["FAL"]="control,length,position,mode,dest,expression"; names["FSC"]="control,length,position,mode,expression";
  names["COP"]="source,dest,length"; names["CPS"]="source,dest,length"; names["FLL"]="source,dest,length";
  names["AVE"]="array,dimToVary,dest,control,length,position"; names["SRT"]="array,dimToVary,control,length,position"; names["STD"]="array,dimToVary,dest,control,length,position"; names["SIZE"]="source,dimToVary,size";
  names["BSL"]="array,control,sourceBit,length"; names["BSR"]="array,control,sourceBit,length";
  names["FFL"]="source,fifo,control,length,position"; names["FFU"]="fifo,dest,control,length,position"; names["LFL"]="source,lifo,control,length,position"; names["LFU"]="lifo,dest,control,length,position";
  names["SQI"]="array,mask,source,control,length,position"; names["SQO"]="array,mask,dest,control,length,position"; names["SQL"]="array,source,control,length,position";
  names["JMP"]="label"; names["LBL"]="label"; names["JSR"]="routine,inputs"; names["JXR"]="routine,input,returnPar,returnPar2"; names["SBR"]="routine,inputs"; names["RET"]="returns";
  names["EOT"]="stateBit"; names["SFP"]="routine,targetState"; names["SFR"]="routine,step"; names["EVENT"]="task";
  names["FOR"]="routine,index,initial,terminal,step";
  names["FBC"]="source,reference,result,cmpControl,length,position,resultControl,length2,position2"; names["DDT"]="source,reference,result,cmpControl,length,position,resultControl,length2,position2"; names["DTR"]="source,mask,reference";
  names["PID"]="pid,processVariable,tieback,controlVariable,pidMasterLoop,inholdBit,inholdValue";
  names["SIN"]="source,dest"; names["COS"]="source,dest"; names["TAN"]="source,dest"; names["ASN"]="source,dest"; names["ACS"]="source,dest"; names["ATN"]="source,dest"; names["LN"]="source,dest"; names["LOG"]="source,dest"; names["XPY"]="sourceA,sourceB,dest"; names["DEG"]="source,dest"; names["RAD"]="source,dest"; names["TOD"]="source,dest"; names["FRD"]="source,dest"; names["TRN"]="source,dest";
  names["MSG"]="message"; names["GSV"]="className,instanceName,attributeName,dest"; names["SSV"]="className,instanceName,attributeName,source"; names["IOT"]="outputTag";
  names["ABL"]="channel,serialControl,charactersCounted"; names["ACB"]="channel,serialControl,charactersCounted"; names["ACL"]="channel,clearSerialPortRead,clearSerialPortWrite";
  names["AHL"]="channel,andMask,orMask,serialControl,channelStatus"; names["ARD"]="channel,dest,serialControl,stringLength,charactersRead"; names["ARL"]="channel,dest,serialControl,stringLength,charactersRead";
  names["AWA"]="channel,source,serialControl,stringLength,charactersSent"; names["AWT"]="channel,source,serialControl,stringLength,charactersSent";
  names["CONCAT"]="sourceA,sourceB,dest"; names["DELETE"]="source,quantity,start,dest"; names["FIND"]="source,search,start,result"; names["INSERT"]="sourceA,sourceB,start,dest"; names["MID"]="source,quantity,start,dest";
  names["STOD"]="source,dest"; names["STOR"]="source,dest"; names["DTOS"]="source,dest"; names["RTOS"]="source,dest"; names["UPPER"]="source,dest"; names["LOWER"]="source,dest";
  names["ALMA"]="alarm,in,progAck,progReset,progDisable,progEnable"; names["ALMD"]="alarm,in,progAck,progReset,progDisable,progEnable";
  names["PCMD"]="phase,command,result"; names["POVR"]="phase,command,result"; names["PATT"]="phase,result"; names["PDET"]="phase"; names["PCLF"]="phase"; names["PFL"]="failureCode"; names["PXRQ"]="phaseInstruction,externalRequest,dataValue";
  always="TON TOF RTO CTU CTD OTE PID ALMA ALMD MAOC MDOC SFP SFR EOT PXRQ PIDE MSG";
  n=split(always, arr, " "); for (i=1;i<=n;i++) alw[arr[i]]=1;
  print "{";
  print "  \"$comment\": \"Logix instruction / service catalog for plc-ir 2.0 sysCall nodes with catalog = logix. Generated from src/L5x/Parsing/Rll/InstructionSignatures.txt (1756-RM014 neutral text table) and hand-grouped; parameter names from 1756-RM003 where known, argN otherwise. lowered = true marks instructions the Logix front-end never emits as sysCall (they become contacts, coils, assignments, standard function blocks, copies or action calls). enable = input marks instructions that run every scan and read the rung condition; gate marks instructions that run only when the rung condition is true.\",";
  print "  \"id\": \"logix\",";
  print "  \"services\": [";
  first=1; group="misc";
}
/^# ---/ { g=$0; sub(/^# --- */,"",g); sub(/ *---.*$/,"",g);
  if (g ~ /^Bit/) group="bit"; else if (g ~ /^Timer/) group="timer"; else if (g ~ /^Compare/) group="compare"; else if (g ~ /^Compute/) group="math";
  else if (g ~ /^Move/) group="move"; else if (g ~ /^Array/) group="file"; else if (g ~ /^Sequencer/) group="file"; else if (g ~ /^Program/) group="program"; else if (g ~ /^For/) group="program";
  else if (g ~ /^Special/) group="file"; else if (g ~ /^Trig/) group="math"; else if (g ~ /^Input/) group="system"; else if (g ~ /^ASCII/) group="ascii"; else if (g ~ /^Alarms/) group="control";
  else if (g ~ /^Equipment/) group="phase"; else if (g ~ /^Motion/) group="motion"; else if (g ~ /^Safety/) group="safety"; else group="misc"; next }
/^#/ || /^$/ { next }
{
  split($0, parts, "|"); name=parts[1]; roles=parts[2]; if (seen[name]) next; seen[name]=1;
  grp=group; if (name=="MSG") grp="messaging"; if (name=="PID") grp="control"; if (name=="DTR") grp="compare";
  lowered=0; if (name ~ /^(XIC|XIO|OTE|OTL|OTU|ONS|OSR|OSF|TON|TOF|RTO|CTU|CTD|RES|CMP|EQU|GEQ|GRT|LEQ|LES|NEQ|LIM|MEQ|CPT|ADD|SUB|MUL|DIV|MOD|SQR|NEG|ABS|MOV|MVM|BTD|CLR|SWPB|AND|OR|XOR|NOT|COP|CPS|FLL|JMP|LBL|JSR|SBR|RET|TND|MCR|AFI|NOP|SIN|COS|TAN|ASN|ACS|ATN|LN|LOG|XPY|DEG|RAD|TOD|FRD|TRN|CONCAT|DELETE|FIND|INSERT|MID|UPPER|LOWER|SIZE)$/) lowered=1;
  enable = (name in alw) ? "input" : "gate";
  variadic = (roles ~ /\.\.\./ || grp == "motion") ? "true" : "false"; gsub(/,\.\.\./, "", roles);
  nr = (roles=="") ? 0 : split(roles, rl, ",");
  nn = split(names[name], nl, ",");
  if (!first) print ","; first=0;
  printf "    { \"name\": \"%s\", \"group\": \"%s\", \"enable\": \"%s\"%s%s, \"params\": [", name, grp, enable, (variadic=="true" ? ", \"variadic\": true" : ""), (lowered ? ", \"lowered\": true" : "");
  for (i=1;i<=nr;i++) {
    r=rl[i]; role = (r=="tag") ? "ref" : (r=="keyword") ? "keyword" : (r=="routine") ? "action" : (r=="label") ? "label" : (r=="expression") ? "expression" : (r=="literal") ? "literal" : "any";
    pn = (i<=nn && nl[i]!="") ? nl[i] : sprintf("arg%d", i-1);
    printf "%s{ \"name\": \"%s\", \"role\": \"%s\" }", (i>1?", ":""), pn, role;
  }
  printf " ] }";
}
END { print ""; print "  ]"; print "}"; }
