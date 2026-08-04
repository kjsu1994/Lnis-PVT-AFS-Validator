//
//  Unit tests for sdr_ch.c.
//
#include "test_sdr.h"

#define SQR(x) ((x) * (x))

extern double sdr_t_acq;
extern double sdr_t_acq_ext;
extern double sdr_t_coh;

// pack signed I/Q values into sdr_cpx8_t --------------------------------------
static sdr_cpx8_t pack_cpx8(int re, int im)
{
    return (sdr_cpx8_t)((((uint8_t)im & 0x0F) << 4) | ((uint8_t)re & 0x0F));
}

// allocate zeroed IF data buffer ----------------------------------------------
static sdr_buff_t *new_zero_buff(int N)
{
    sdr_buff_t *buff = sdr_buff_new(N, 2);
    
    TEST_ASSERT_TRUE(buff != NULL);
    for (int i = 0; i < N; i++) {
        buff->data[i] = pack_cpx8(0, 0);
    }
    return buff;
}

// test sdr_ch_new() and sdr_ch_free() -----------------------------------------
static void test_sdr_ch_new_free_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L1CA", 1, 4e6, 0.0);
    
    TEST_ASSERT_TRUE(ch != NULL);
    TEST_ASSERT_EQ_INT(SDR_STATE_IDLE, ch->state);
    TEST_ASSERT_EQ_INT(1, ch->prn);
    TEST_ASSERT_TRUE(!strcmp(ch->sig, "L1CA"));
    TEST_ASSERT_TRUE(!strcmp(ch->sat, "G01"));
    TEST_ASSERT_EQ_INT(1023, ch->len_code);
    TEST_ASSERT_EQ_INT(1, ch->len_sec_code);
    TEST_ASSERT_NEAR(4e6, ch->fs, 1e-6);
    TEST_ASSERT_NEAR(1e-3, ch->T, 1e-15);
    TEST_ASSERT_EQ_INT(4000, ch->N);
    TEST_ASSERT_TRUE(ch->acq != NULL);
    TEST_ASSERT_TRUE(ch->trk != NULL);
    TEST_ASSERT_TRUE(ch->nav != NULL);
    TEST_ASSERT_TRUE(ch->data != NULL);
    TEST_ASSERT_EQ_INT(4, ch->trk->npos);
    TEST_ASSERT_EQ_INT(0, ch->trk->nposx);
    TEST_ASSERT_EQ_INT(0, ch->pilot);
    sdr_ch_free(ch);
    
    ch = sdr_ch_new("l1ca", 1, 4e6, 0.0);
    TEST_ASSERT_TRUE(ch != NULL);
    TEST_ASSERT_TRUE(!strcmp(ch->sig, "L1CA"));
    sdr_ch_free(ch);
    
    TEST_ASSERT_TRUE(sdr_ch_new("UNKNOWN", 1, 4e6, 0.0) == NULL);
    TEST_ASSERT_TRUE(sdr_ch_new("L1CA", 0, 4e6, 0.0) == NULL);
    sdr_ch_free(NULL);
}

// test sdr_ch_set_corr() ------------------------------------------------------
static void test_sdr_ch_set_corr_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L1CA", 1, 4e6, 0.0);
    double stat[8], pos[SDR_MAX_CORR], P[SDR_MAX_CORR], I[SDR_MAX_CORR];
    sdr_cpx_t C[SDR_MAX_CORR];
    int npos;
    
    TEST_ASSERT_TRUE(ch != NULL);
    
    sdr_ch_set_corr(ch, 5, 2e-6);
    npos = sdr_ch_corr_stat(ch, stat, pos, C, P, I);
    
    TEST_ASSERT_EQ_INT(9, npos);
    TEST_ASSERT_EQ_INT(4, (int)stat[6]);
    TEST_ASSERT_EQ_INT(5, ch->trk->nposx);
    TEST_ASSERT_NEAR(-3.2, pos[4], 1e-9);
    TEST_ASSERT_NEAR(0.0, pos[6], 1e-9);
    TEST_ASSERT_NEAR(3.2, pos[8], 1e-9);
    
    sdr_ch_set_corr(ch, SDR_N_CORRX + 10, 1e-6);
    npos = sdr_ch_corr_stat(ch, stat, pos, C, P, NULL);
    TEST_ASSERT_EQ_INT(4 + SDR_N_CORRX, npos);
    TEST_ASSERT_EQ_INT(SDR_N_CORRX, ch->trk->nposx);
    
    sdr_ch_free(ch);
}

// test sdr_ch_corr_stat() -----------------------------------------------------
static void test_sdr_ch_corr_stat_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L1CA", 1, 4e6, 0.0);
    double stat[8], pos[SDR_MAX_CORR], P[SDR_MAX_CORR], I[SDR_MAX_CORR];
    sdr_cpx_t C[SDR_MAX_CORR];
    int npos;
    
    TEST_ASSERT_TRUE(ch != NULL);
    
    ch->state = SDR_STATE_LOCK;
    ch->lock = 7;
    ch->cn0 = 42.5;
    ch->coff = 2.5e-4;
    ch->fd = -123.0;
    ch->trk->C[0][0] = 10.0f;
    ch->trk->C[0][1] = -2.0f;
    ch->trk->aveP[0] = 25.0;
    ch->trk->aveI[0] = 4.0;
    
    npos = sdr_ch_corr_stat(ch, stat, pos, C, P, I);
    
    TEST_ASSERT_EQ_INT(4, npos);
    TEST_ASSERT_EQ_INT(SDR_STATE_LOCK, (int)stat[0]);
    TEST_ASSERT_NEAR(4e6, stat[1], 1e-6);
    TEST_ASSERT_NEAR(7e-3, stat[2], 1e-12);
    TEST_ASSERT_NEAR(42.5, stat[3], 1e-12);
    TEST_ASSERT_NEAR(0.25, stat[4], 1e-12);
    TEST_ASSERT_NEAR(-123.0, stat[5], 1e-12);
    TEST_ASSERT_EQ_INT(4, (int)stat[6]);
    TEST_ASSERT_NEAR(10.0, C[0][0], 1e-6);
    TEST_ASSERT_NEAR(-2.0, C[0][1], 1e-6);
    TEST_ASSERT_NEAR(25.0, P[0], 1e-12);
    TEST_ASSERT_NEAR(4.0, I[0], 1e-12);
    
    sdr_ch_free(ch);
}

// test sdr_ch_corr_hist() -----------------------------------------------------
static void test_sdr_ch_corr_hist_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L1CA", 1, 4e6, 0.0);
    double stat[2];
    sdr_cpx_t P[SDR_N_HIST];
    int n;
    
    TEST_ASSERT_TRUE(ch != NULL);
    
    ch->time = 12.25;
    for (int i = 0; i < SDR_N_HIST; i++) {
        ch->trk->P[i][0] = (float)i;
        ch->trk->P[i][1] = (float)-i;
    }
    
    n = sdr_ch_corr_hist(ch, 3.5 * ch->T, stat, P);
    TEST_ASSERT_EQ_INT(3, n);
    TEST_ASSERT_NEAR(12.25, stat[0], 1e-12);
    TEST_ASSERT_NEAR(ch->T, stat[1], 1e-15);
    TEST_ASSERT_NEAR(SDR_N_HIST - 3, P[0][0], 1e-6);
    TEST_ASSERT_NEAR(-(SDR_N_HIST - 3), P[0][1], 1e-6);
    TEST_ASSERT_NEAR(SDR_N_HIST - 1, P[2][0], 1e-6);
    
    n = sdr_ch_corr_hist(ch, 10000.0 * ch->T, stat, P);
    TEST_ASSERT_EQ_INT(SDR_N_HIST, n);
    
    sdr_ch_free(ch);
}

// test sdr_ch_update() in IDLE state ------------------------------------------
static void test_sdr_ch_update_idle_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L1CA", 1, 4e6, 0.0);
    sdr_buff_t *buff;
    
    TEST_ASSERT_TRUE(ch != NULL);
    buff = new_zero_buff(ch->N * 2);
    
    sdr_ch_update(ch, 1.0, buff, 0);
    
    TEST_ASSERT_EQ_INT(SDR_STATE_IDLE, ch->state);
    TEST_ASSERT_EQ_INT(0, ch->lock);
    TEST_ASSERT_NEAR(0.0, ch->time, 1e-12);
    
    sdr_buff_free(buff);
    sdr_ch_free(ch);
}

// test sdr_ch_update() in SEARCH state ----------------------------------------
static void test_sdr_ch_update_search_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L1CA", 1, 4e6, 0.0);
    sdr_buff_t *buff;
    double old_t_acq = sdr_t_acq;
    
    TEST_ASSERT_TRUE(ch != NULL);
    buff = new_zero_buff(ch->N * 2);
    
    sdr_t_acq = ch->T;
    ch->state = SDR_STATE_SRCH;
    sdr_ch_update(ch, 1.0, buff, 0);
    
    TEST_ASSERT_EQ_INT(SDR_STATE_IDLE, ch->state);
    TEST_ASSERT_EQ_INT(0, ch->acq->n_sum);
    TEST_ASSERT_TRUE(ch->acq->P_sum == NULL);
    
    sdr_t_acq = old_t_acq;
    sdr_buff_free(buff);
    sdr_ch_free(ch);
}

// test sdr_ch_update() in assisted SEARCH state -------------------------------
static void test_sdr_ch_update_assisted_search_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L1CA", 1, 4e6, 0.0);
    sdr_buff_t *buff;
    double old_t_acq_ext = sdr_t_acq_ext;

    TEST_ASSERT_TRUE(ch != NULL);
    buff = new_zero_buff(ch->N * 2);

    sdr_t_acq_ext = ch->T;
    ch->acq->fd_ext = 0.0f;
    ch->acq->fd_ext_valid = 1;
    ch->state = SDR_STATE_SRCH;
    sdr_ch_update(ch, 1.0, buff, 0);

    TEST_ASSERT_EQ_INT(SDR_STATE_IDLE, ch->state);
    TEST_ASSERT_EQ_INT(0, ch->acq->n_sum);
    TEST_ASSERT_TRUE(ch->acq->P_sum == NULL);
    TEST_ASSERT_NEAR(0.0, ch->acq->fd_ext, 1e-6);
    TEST_ASSERT_EQ_INT(0, ch->acq->fd_ext_valid);

    sdr_t_acq_ext = old_t_acq_ext;
    sdr_buff_free(buff);
    sdr_ch_free(ch);
}

// test sdr_ch_update() in LOCK state ------------------------------------------
static void test_sdr_ch_update_lock_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L1CA", 1, 4e6, 0.0);
    sdr_buff_t *buff;
    
    TEST_ASSERT_TRUE(ch != NULL);
    buff = new_zero_buff(ch->N * 2);
    
    ch->state = SDR_STATE_LOCK;
    ch->time = 0.0;
    ch->fd = 0.0;
    ch->coff = 0.0;
    ch->cn0 = 45.0;
    
    sdr_ch_update(ch, ch->T, buff, 0);
    
    TEST_ASSERT_EQ_INT(SDR_STATE_LOCK, ch->state);
    TEST_ASSERT_EQ_INT(1, ch->lock);
    TEST_ASSERT_NEAR(ch->T, ch->time, 1e-12);
    TEST_ASSERT_TRUE(isfinite(ch->fd));
    TEST_ASSERT_TRUE(isfinite(ch->coff));
    
    sdr_buff_free(buff);
    sdr_ch_free(ch);
}

// test coherent prompt accumulation for a synchronized pilot -----------------
static void test_sdr_ch_update_pilot_coherent_api(void)
{
    sdr_ch_t *ch = sdr_ch_new("L5Q", 1, 4e6, 0.0);
    sdr_buff_t *buff;
    double old_t_coh = sdr_t_coh;

    TEST_ASSERT_TRUE(ch != NULL);
    TEST_ASSERT_EQ_INT(1, ch->pilot);
    buff = new_zero_buff(ch->N * 2);

    sdr_t_coh = 0.02;
    ch->state = SDR_STATE_LOCK;
    ch->time = 2.001;
    ch->lock = 2001;
    ch->cn0 = 45.0;
    ch->trk->sec_sync = 2000;
    ch->trk->sec_pol = 1;

    sdr_ch_update(ch, ch->time + ch->T, buff, 0);

    TEST_ASSERT_EQ_INT(1, ch->trk->coh_n);
    TEST_ASSERT_TRUE(isfinite(ch->fd));

    sdr_t_coh = old_t_coh;
    sdr_buff_free(buff);
    sdr_ch_free(ch);
}

// test sdr_ch_update() with synthetic L6D/E CSK signal -------------------------
static void test_ch_update_l6_csk(const char *sig, int prn)
{
    static const int shift[] = {0, 1, 2, 3, 5, 37, 128, 254, 255}; // (chips)
    int n_sym = (int)(sizeof(shift) / sizeof(shift[0]));
    int len_code = 0, coff_smp = 33;
    uint32_t lcg = 12345;
    double fs = 12e6;
    const int8_t *code = sdr_gen_code(sig, prn, &len_code);
    sdr_ch_t *ch = sdr_ch_new(sig, prn, fs, 0.0);

    TEST_ASSERT_TRUE(code != NULL);
    TEST_ASSERT_EQ_INT(20460, len_code);
    TEST_ASSERT_TRUE(ch != NULL);

    // generate noisy L6D/E CSK IF signal (zero-IF)
    sdr_buff_t *buff = new_zero_buff(coff_smp + (n_sym + 1) * ch->N);
    for (int n = coff_smp; n < buff->N; n++) {
        int m = (n - coff_smp) / ch->N;
        int j = (n - coff_smp) % ch->N;
        if (m >= n_sym) m = n_sym - 1;
        int slot = (int)((double)j * len_code / ch->N);
        int s = (slot - 2 * shift[m]) % len_code;
        if (s < 0) s += len_code;
        lcg = lcg * 1103515245 + 12345;
        int nI = (int)((lcg >> 24) % 13) - 6; // deterministic noise [-6,6]
        int nQ = (int)((lcg >> 16) % 13) - 6;
        buff->data[n] = pack_cpx8(code[s] + nI, nQ); // noise-dominated signal
    }
    ch->state = SDR_STATE_LOCK;
    ch->coff = coff_smp / fs;

    for (int m = 0; m < n_sym; m++) {
        sdr_ch_update(ch, (m + 1) * ch->T, buff, m * ch->N);

        // decoded CSK symbol
        uint8_t sym = ch->nav->syms[SDR_MAX_NSYM-1];
        TEST_ASSERT_EQ_INT(255 - shift[m], sym);

        // EPL correlations of CSK-shifted code
        double P = sqrt(SQR(ch->trk->C[0][0]) + SQR(ch->trk->C[0][1]));
        double E = sqrt(SQR(ch->trk->C[1][0]) + SQR(ch->trk->C[1][1]));
        double L = sqrt(SQR(ch->trk->C[2][0]) + SQR(ch->trk->C[2][1]));
        TEST_ASSERT_TRUE(P > 0.3);
        TEST_ASSERT_TRUE(ch->trk->C[0][0] > 0.0f); // in-phase and no flip
        TEST_ASSERT_TRUE(fabs(ch->trk->C[0][1]) < 0.2 * P);
        TEST_ASSERT_TRUE(E > 0.4 * P && E < P);
        TEST_ASSERT_TRUE(L > 0.4 * P && L < P);
        TEST_ASSERT_TRUE(fabs(E - L) < 0.3 * P);

        if (m == 0) { // emulate L6 frame sync to narrow CSK peak search
            ch->nav->fsync = ch->lock;
            ch->nav->coff = -ch->T / 10230; // symbol offset = 0
        }
        if (m >= 2) { // code shift reference fixed after frame sync
            TEST_ASSERT_EQ_INT(255, ch->trk->csk_ref);
        }
    }
    sdr_buff_free(buff);
    sdr_ch_free(ch);
}

// test sdr_ch_update() with L6D/L6E CSK signals --------------------------------
static void test_sdr_ch_update_l6_csk(void)
{
    test_ch_update_l6_csk("L6D", 194);
    test_ch_update_l6_csk("L6E", 204);
}

// main ------------------------------------------------------------------------
int main(void)
{
    sdr_func_init("");

    TEST_RUN(test_sdr_ch_new_free_api);
    TEST_RUN(test_sdr_ch_set_corr_api);
    TEST_RUN(test_sdr_ch_corr_stat_api);
    TEST_RUN(test_sdr_ch_corr_hist_api);
    TEST_RUN(test_sdr_ch_update_idle_api);
    TEST_RUN(test_sdr_ch_update_search_api);
    TEST_RUN(test_sdr_ch_update_assisted_search_api);
    TEST_RUN(test_sdr_ch_update_lock_api);
    TEST_RUN(test_sdr_ch_update_pilot_coherent_api);
    TEST_RUN(test_sdr_ch_update_l6_csk);

    return 0;
}
