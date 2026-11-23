# µÚÒ»½×¶Î½ÓÈëÓëÑéÖ¤

µ±Ç°°æ±¾ÊÇºËÐÄ¹Ç¼Ü£¬²»º¬ÈÎºÎÕæÊµÆ½Ì¨ SDK¡£Unity »ùÏß 2022.3£»Ê×´Î¿ÉÓÃ·¢²¼Ç°ÈÔÐè Unity µ¼Èë¼°Õæ»úÑéÖ¤¡£

Í¨¹ý Unity Package Manager µÄ Add package from disk Ñ¡Ôñ `Packages/com.erdon.unitysdk/package.json`¡£µ¼Èë Mock Login Ê¾Àý£¬½« MockLoginExample ¹Òµ½³¡¾°¶ÔÏó£¬ÔÚ Editor Play ²é¿´½á¹û£»Ê¾Àý½öÔÚ Editor ±àÒë£¬²»ÊÇÕæÊµÕËºÅ¡£

³õÊ¼»¯ÔÝÓÉ `UnitySdkHost.Initialize(config, provider)` ÏÔÊ½×¢Èë IAuth¡£ºóÐøÅäÖÃÃæ°åÉú³ÉÆ½Ì¨×¢²á£¬²»ÒªÇóÓÎÏ·×Ô¼ºÑ¡ÔñÊµÏÖ¡£Initialize / Login / Shutdown Ä¿Ç°ÒªÇóÔÚ Unity Ö÷Ïß³Ìµ÷ÓÃ£»Òì²½Æ½Ì¨Íê³ÉÍ¨¹ýÖ÷Ïß³Ì¶ÓÁÐÅÉ·¢¡£Shutdown ºó¾É²Ù×÷»Øµ÷±»¶ªÆú£»µÈ´ýµ÷ÓÃ·½ÐèÍ¬²½½áÊø×Ô¼ºµÄ¼ÓÔØ×´Ì¬¡£

`SDK.Login(callback)`£ºÎÞ²ÎÊý£»·µ»Ø LoginResult¡£³É¹¦ Session Îª»á»°Æ¾¾Ý£¬½ûÖ¹Êä³ö¡£¿ÉÄÜ´íÎó£ºSDK.NOT_INITIALIZED¡¢MODULE.DISABLED¡¢CAPABILITY.NOT_SUPPORTED¡¢AUTH.IN_PROGRESS¡¢AUTH.UNKNOWN£¬ÒÔ¼°ÊÊÅäÆ÷¶¨ÒåµÄÒÑÓ³Éä´íÎó¡£Ä¬ÈÏ³¬Ê± 15000ms£»Login(callback, timeoutMilliseconds) ¿ÉÖ¸¶¨ÕýÊý³¬Ê±¡£CancelLogin() ½áÊø±¾µØµÈ´ý£¬·µ»Ø AUTH.CANCELLED£¬²»±£Ö¤³·ÏúÆ½Ì¨ÊÚÈ¨¡£³¬Ê±·µ»Ø AUTH.TIMEOUT¡£Íê³ÉÐÅºÅÒÔµÚÒ»¸öÎª×¼£¬³Ùµ½»Øµ÷ºöÂÔ¡£UnitySdkHost ÔÚ Update ×Ô¶¯µ÷ÓÃ Tick£»ÔÝÍ£ÆÚ¼ä²»ÅÉ·¢£¬»Ö¸´ºó¼ì²é³¬Ê±¡£

`SdkConfig.LogCallback = (level, message) => GameLogger.Write(level, message)`£ºÕýÎÄÓÉ SDK Ìá¹©£»²»ÉèÖÃÔò Unity Debug Êä³ö¡£»Øµ÷Òì³£»ØÍËÄ¬ÈÏÈÕÖ¾¡£µ±Ç°µÇÂ¼ÎÞÈë²Î£¬params Îª {}£»Æ¾¾Ý¼°ÈÎÒâÆ½Ì¨Ô­Ê¼Òì³£²»Ð´ÈÕÖ¾¡£ÕýÊ½Õï¶Ï×Ö¶ÎºÍ¸ü¶àÄÜÁ¦´ýºóÐøµü´ú¡£

ÑéÖ¤£ºtests/CoreTests.cs ÊÇÎÞÍâ²¿ÒÀÀµµÄºËÐÄÐÐÎª²âÊÔ£¬Óë SDK.cs Ò»Æð±àÒëÖ´ÐÐ¡£¸²¸ÇÖØ¸´ºÍ¹ýÆÚ»Øµ÷¡¢Æ¾¾Ý²»Ð´ÈÕÖ¾¡¢ÈÕÖ¾×Ö¶Î¡¢ÈÕÖ¾»Øµ÷Òì³£¡¢Ä£¿é¹Ø±Õ¼°Î´³õÊ¼»¯¡£²»´ú±í Unity µ¼Èë»òÕæÊµÆ½Ì¨ÑéÖ¤¡£

ÈÕÖ¾»Øµ÷Ö»ÓÃÓÚ½ÓÊÕÊä³ö£¬²»ÔÊÐíÖØÈë SDK µÄ³õÊ¼»¯¡¢µÇÂ¼¡¢È¡Ïû¡¢ÊÍ·Å»ò Tick£»ÖØÈë»á´¥·¢Ä¬ÈÏÈÕÖ¾»ØÍË¡£ÓÎÏ·Íê³É»Øµ÷¿ÉÒÔÔÙ´Î·¢ÆðµÇÂ¼£»»Øµ÷Å×Òì³£±»¸ôÀë²¢¼ÇÂ¼ CALLBACK.EXCEPTION£¬²»Êä³öÔ­Òì³£ÄÚÈÝ¡£

### é”™è¯¯å…³è”å­—æ®µï¼ˆå·²å®žçŽ°ï¼‰

LoginResult.CallId ç”± SDK åœ¨æ´¾å‘æ—¶å¡«å†™ï¼Œä¸Žè¯¥è°ƒç”¨æ—¥å¿—ä¸€è‡´ã€‚å¤±è´¥æ—¶ Error åŒ…å« Codeã€Reasonã€Operationã€Stageã€CallIdã€PlatformCodeã€PlatformMessageï¼›é€‚é…å™¨å¿…é¡»åªæäº¤å·²è„±æ•çš„ PlatformMessageã€‚å½“å‰ Stage é»˜è®¤ Loginï¼ŒåŽç»­çœŸå®žé€‚é…ç»†åŒ–ä¸ºæŽˆæƒ/å‡­è¯äº¤æ¢ç­‰é˜¶æ®µã€‚å¹³å°åŽŸå§‹å†…å®¹ä¸è‡ªåŠ¨å†™å…¥æ—¥å¿—ï¼Œé¿å…ç¬¬ä¸‰æ–¹å¼‚å¸¸æºå¸¦å‡­æ®ã€‚

åˆ›å»ºå¤±è´¥ï¼š`LoginResult.Fail(new SdkError("AUTH.PERMISSION_DENIED", "æŽˆæƒè¢«æ‹’ç»", "åŽŸå§‹ç ", "è„±æ•è¯´æ˜Ž"))`ã€‚æ¸¸æˆæ ¹æ® Code åˆ¤æ–­ä¸šåŠ¡ï¼Œä¸è§£æž PlatformMessageã€‚æœªçŸ¥é”™è¯¯ä¸æŽ¨æ–­è´¦å·å°ç¦æˆ–åœ°åŒºé™åˆ¶ã€‚å®Œæ•´é‡è¯•ç­–ç•¥ä¸Ž traceId å°šå¾…å®žçŽ°ã€‚
