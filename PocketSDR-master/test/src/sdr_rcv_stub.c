//
//  Minimal receiver wrapper stubs for isolated unit tests.
//
#include "test_sdr.h"

void sdr_rcv_array_calib(sdr_rcv_t *rcv, const obsd_t *obs, int nobs,
    const nav_t *nav, const double *rr)
{
    (void)rcv;
    (void)obs;
    (void)nobs;
    (void)nav;
    (void)rr;
}

int sdr_rcv_write_str(sdr_rcv_t *rcv, int type, uint8_t *data, int size)
{
    int bytes = -1;

    for (int i = 0; i < SDR_MAX_STR; i++) {
        if (!rcv->strs[i] || rcv->str_type[i] != type) continue;
        int n = sdr_str_write(rcv->strs[i], data, size);
        if (bytes < 0) bytes = n;
    }
    return bytes < 0 ? 0 : bytes;
}
