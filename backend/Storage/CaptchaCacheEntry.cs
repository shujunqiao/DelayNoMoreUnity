﻿namespace backend.Storage ;
public class CaptchaCacheEntry {
    public string Captcha;
    public int PlayerId;

    public CaptchaCacheEntry(string captcha, int playerId) {
        Captcha = captcha;
        PlayerId = playerId;
    }

}
