// wotlk343hook.dylib: injected into the macOS 3.4.3 client by the WotLK343 launcher
// (DYLD_INSERT_LIBRARIES). The client's code is encrypted on disk, so instead of patching its
// certificate checks like Burralis does on Windows, this interposes Apple's SecTrustEvaluate and
// additionally accepts exactly one certificate: the localhost certificate HermesProxy serves, pinned
// by its SHA-256 (WOTLK343_PIN). Every other certificate gets the normal macOS verdict.
// It also logs TLS/DNS activity to WOTLK343_HOOKLOG for troubleshooting.
// Built without an SDK, so the few system declarations needed are written out here.

typedef unsigned char Boolean;
typedef int OSStatus;
typedef unsigned int uint32_t;
typedef long CFIndex;
typedef unsigned long size_t;
typedef const void *CFTypeRef;
typedef const struct __CFData *CFDataRef;
typedef const struct __CFArray *CFArrayRef;
typedef const struct __CFString *CFStringRef;
typedef struct __SecTrust *SecTrustRef;
typedef struct __SecCertificate *SecCertificateRef;
typedef struct SSLContext *SSLContextRef;
typedef uint32_t SecTrustResultType;
typedef struct __sFILE FILE;
struct addrinfo;

enum { kSecTrustResultProceed = 1, kSecTrustResultUnspecified = 4, kCFStringEncodingUTF8 = 0x08000100 };

extern OSStatus SecTrustEvaluate(SecTrustRef, SecTrustResultType *);
extern OSStatus SecTrustSetAnchorCertificates(SecTrustRef, CFArrayRef);
extern OSStatus SecTrustSetAnchorCertificatesOnly(SecTrustRef, Boolean);
extern CFIndex SecTrustGetCertificateCount(SecTrustRef);
// deprecated or newer APIs are weak imports: a macOS without one binds it to NULL instead of
// refusing to load the game
extern SecCertificateRef SecTrustGetCertificateAtIndex(SecTrustRef, CFIndex) __attribute__((weak_import));
extern CFArrayRef SecTrustCopyCertificateChain(SecTrustRef) __attribute__((weak_import));
extern const void *CFArrayGetValueAtIndex(CFArrayRef, CFIndex);
extern CFDataRef SecCertificateCopyData(SecCertificateRef);
extern CFStringRef SecCertificateCopySubjectSummary(SecCertificateRef);
extern OSStatus SSLHandshake(SSLContextRef) __attribute__((weak_import));
extern OSStatus SSLSetPeerDomainName(SSLContextRef, const char *, size_t) __attribute__((weak_import));
extern const unsigned char *CFDataGetBytePtr(CFDataRef);
extern CFIndex CFDataGetLength(CFDataRef);
extern CFIndex CFArrayGetCount(CFArrayRef);
extern Boolean CFStringGetCString(CFStringRef, char *, CFIndex, uint32_t);
extern void CFRelease(CFTypeRef);
extern unsigned char *CC_SHA256(const void *, uint32_t, unsigned char *);
extern int getaddrinfo(const char *, const char *, const struct addrinfo *, struct addrinfo **);
extern char *getenv(const char *);
extern FILE *fopen(const char *, const char *);
extern int fputs(const char *, FILE *);
extern int fflush(FILE *);
extern int snprintf(char *, size_t, const char *, ...);
extern int getpid(void);

static FILE *logf_;

static void logline(const char *s) {
    if (!logf_) {
        const char *p = getenv("WOTLK343_HOOKLOG");
        logf_ = fopen(p && *p ? p : "/tmp/wotlk343-hook.log", "a");
        if (!logf_) return;
    }
    fputs(s, logf_);
    fputs("\n", logf_);
    fflush(logf_);
}

// the leaf certificate (caller releases *keep when set)
static SecCertificateRef leaf_of(SecTrustRef trust, CFArrayRef *keep) {
    *keep = 0;
    if (!trust || SecTrustGetCertificateCount(trust) < 1) return 0;
    if (SecTrustCopyCertificateChain) {
        CFArrayRef chain = SecTrustCopyCertificateChain(trust);
        if (!chain || CFArrayGetCount(chain) < 1) { if (chain) CFRelease(chain); return 0; }
        *keep = chain;
        return (SecCertificateRef)CFArrayGetValueAtIndex(chain, 0);
    }
    return SecTrustGetCertificateAtIndex ? SecTrustGetCertificateAtIndex(trust, 0) : 0;
}

static void subject(SecCertificateRef c, char *out, int n) {
    out[0] = 0;
    CFStringRef s = c ? SecCertificateCopySubjectSummary(c) : 0;
    if (s) { CFStringGetCString(s, out, n, kCFStringEncodingUTF8); CFRelease(s); }
}

// true if the leaf certificate's SHA-256 is listed in WOTLK343_PIN (hex, comma-separated)
static int leaf_pinned(SecCertificateRef leaf, char *hexout) {
    hexout[0] = 0;
    const char *pins = getenv("WOTLK343_PIN");
    CFDataRef der = leaf ? SecCertificateCopyData(leaf) : 0;
    if (!der) return 0;
    unsigned char h[32];
    CC_SHA256(CFDataGetBytePtr(der), (uint32_t)CFDataGetLength(der), h);
    CFRelease(der);
    static const char hx[] = "0123456789abcdef";
    for (int i = 0; i < 32; i++) { hexout[2 * i] = hx[h[i] >> 4]; hexout[2 * i + 1] = hx[h[i] & 15]; }
    hexout[64] = 0;
    if (!pins) return 0;
    for (const char *p = pins; *p;) {
        int i = 0;
        while (i < 64 && p[i] && (p[i] | 0x20) == hexout[i]) i++;
        if (i == 64 && (p[64] == 0 || p[64] == ',')) return 1;
        while (*p && *p != ',') p++;
        if (*p == ',') p++;
    }
    return 0;
}

static OSStatus my_SecTrustEvaluate(SecTrustRef trust, SecTrustResultType *result) {
    SecTrustResultType r = 0;
    OSStatus st = SecTrustEvaluate(trust, &r);
    char hex[65], subj[256], line[512];
    CFArrayRef keep;
    SecCertificateRef leaf = leaf_of(trust, &keep);
    int pinned = leaf_pinned(leaf, hex);
    subject(leaf, subj, sizeof subj);
    if (keep) CFRelease(keep);
    int ok = st == 0 && (r == kSecTrustResultProceed || r == kSecTrustResultUnspecified);
    if (!ok && pinned) { st = 0; r = kSecTrustResultUnspecified; }
    snprintf(line, sizeof line, "SecTrustEvaluate leaf='%s' sha256=%s certs=%ld macOS=%s pinned=%d -> status=%d result=%u",
             subj, hex, trust ? (long)SecTrustGetCertificateCount(trust) : 0L, ok ? "trusted" : "untrusted", pinned, st, r);
    logline(line);
    if (result) *result = r;
    return st;
}

static OSStatus my_SecTrustSetAnchorCertificates(SecTrustRef trust, CFArrayRef anchors) {
    char line[160];
    snprintf(line, sizeof line, "SecTrustSetAnchorCertificates count=%ld", anchors ? (long)CFArrayGetCount(anchors) : -1L);
    logline(line);
    return SecTrustSetAnchorCertificates(trust, anchors);
}

static OSStatus my_SecTrustSetAnchorCertificatesOnly(SecTrustRef trust, Boolean only) {
    char line[96];
    snprintf(line, sizeof line, "SecTrustSetAnchorCertificatesOnly %d", only);
    logline(line);
    return SecTrustSetAnchorCertificatesOnly(trust, only);
}

static OSStatus my_SSLSetPeerDomainName(SSLContextRef ctx, const char *name, size_t len) {
    char line[320];
    snprintf(line, sizeof line, "SSLSetPeerDomainName '%.*s'", (int)(len < 256 ? len : 256), name ? name : "");
    logline(line);
    return SSLSetPeerDomainName ? SSLSetPeerDomainName(ctx, name, len) : -4;  // unimpErr
}

static OSStatus my_SSLHandshake(SSLContextRef ctx) {
    OSStatus st = SSLHandshake ? SSLHandshake(ctx) : -4;
    if (st != -9803) {  // errSSLWouldBlock is just non-blocking I/O
        char line[96];
        snprintf(line, sizeof line, "SSLHandshake -> %d", st);
        logline(line);
    }
    return st;
}

static int my_getaddrinfo(const char *node, const char *service, const struct addrinfo *hints, struct addrinfo **res) {
    int rc = getaddrinfo(node, service, hints, res);
    char line[400];
    snprintf(line, sizeof line, "getaddrinfo '%s' port '%s' -> %d", node ? node : "", service ? service : "", rc);
    logline(line);
    return rc;
}

__attribute__((constructor)) static void start(void) {
    char line[128];
    snprintf(line, sizeof line, "---- wotlk343hook loaded (pid %d), pin %s", getpid(), getenv("WOTLK343_PIN") ? "set" : "MISSING");
    logline(line);
}

typedef struct { const void *replacement, *replacee; } interpose_t;
__attribute__((used, section("__DATA,__interpose"))) static const interpose_t interposers[] = {
    {(const void *)my_SecTrustEvaluate, (const void *)SecTrustEvaluate},
    {(const void *)my_SecTrustSetAnchorCertificates, (const void *)SecTrustSetAnchorCertificates},
    {(const void *)my_SecTrustSetAnchorCertificatesOnly, (const void *)SecTrustSetAnchorCertificatesOnly},
    {(const void *)my_SSLSetPeerDomainName, (const void *)SSLSetPeerDomainName},
    {(const void *)my_SSLHandshake, (const void *)SSLHandshake},
    {(const void *)my_getaddrinfo, (const void *)getaddrinfo},
};
