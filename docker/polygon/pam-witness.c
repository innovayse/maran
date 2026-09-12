/*
 * pam-witness — asks the real PAM library what one service's stack answers for
 * one account and one password, and prints the two answers.
 *
 * DEVELOPMENT ONLY. It is compiled and run inside the polygon images at build
 * time by docker/polygon/assert-installer-steps.sh and ships on no host.
 *
 * Why it exists. `assert_ftps_pam_stack_requires_group_membership` asserts what
 * /etc/pam.d/maran-ftps CONTAINS. A PAM stack is not its lines, it is their
 * control flow: prepending `auth sufficient pam_permit.so` to the shipped file
 * leaves both `required pam_succeed_if` lines byte-identical, and turns the
 * daemon's authorization into "every account on the host, password optional".
 * Measured on 2026-09-09 on ubuntu24 and alma9 with this program: through the
 * mutated stack an account that is in no group and typed the WRONG password
 * gets `authenticate=0 Success`. Every content check stayed green over it. So
 * the only thing that can observe the property the file exists to hold is the
 * library that reads it, and this is the smallest program that asks.
 *
 * What it is NOT: it is not vsftpd. It performs the same two PAM transactions a
 * daemon performs before it accepts a login — `pam_authenticate` and
 * `pam_acct_mgmt`, both phases the shipped stack carries the group test in —
 * against the same service name (`pam_service_name` in the agent's rendered
 * vsftpd.conf). What no image build can observe is the daemon's own behaviour
 * around them: the TLS handshake, the chroot, the data channel. Those stay for
 * ftps_on_a_real_host.rs.
 *
 * Output, on stdout, one per line, so a shell can read a code rather than parse
 * prose:
 *
 *   authenticate=<code> <pam_strerror text>
 *   acct_mgmt=<code> <pam_strerror text>
 *
 * Exit: 0 when BOTH phases returned PAM_SUCCESS — that is, the stack accepted
 * this login. 1 when either refused. 2 for a usage error and 3 when pam_start
 * itself failed, which is a broken harness rather than a refusal and must never
 * be read as one.
 */
#include <security/pam_appl.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/*
 * The password this run was asked to offer. A file-scope pointer because a PAM
 * conversation function receives its context through `pam_conv.appdata_ptr`,
 * and this program has exactly one conversation in flight for its whole life.
 */
static const char *witness_password;

/*
 * witness_conversation — answers every prompt the stack raises with the one
 * password given on the command line, and ignores informational and error
 * messages.
 *
 * A module may raise several messages in one call (the count argument), so the
 * response array is allocated per call and one entry is filled per message: PAM
 * frees the array and every string in it. Anything that is not a prompt gets a
 * NULL response, which is what a non-interactive caller owes those styles.
 */
static int witness_conversation(int count, const struct pam_message **messages,
                                struct pam_response **responses, void *appdata)
{
    struct pam_response *replies;
    int index;

    (void)appdata;
    if (count <= 0 || messages == NULL || responses == NULL) {
        return PAM_CONV_ERR;
    }
    replies = calloc((size_t)count, sizeof(*replies));
    if (replies == NULL) {
        return PAM_BUF_ERR;
    }
    for (index = 0; index < count; index++) {
        replies[index].resp_retcode = 0;
        replies[index].resp = NULL;
        switch (messages[index]->msg_style) {
        case PAM_PROMPT_ECHO_OFF:
        case PAM_PROMPT_ECHO_ON:
            replies[index].resp = strdup(witness_password);
            if (replies[index].resp == NULL) {
                while (index-- > 0) {
                    free(replies[index].resp);
                }
                free(replies);
                return PAM_BUF_ERR;
            }
            break;
        default:
            break;
        }
    }
    *responses = replies;
    return PAM_SUCCESS;
}

/*
 * main — one PAM transaction: start the named service for the named user,
 * authenticate with the given password, then ask the account phase whether that
 * user may use this service at all.
 *
 * Both phases are performed and both are printed even when the first refuses,
 * because they answer different questions and a stack can hold one and lose the
 * other: the group test is repeated in the account phase precisely so that an
 * auth-phase pass is not the whole answer.
 */
int main(int argc, char **argv)
{
    struct pam_conv conversation = { witness_conversation, NULL };
    pam_handle_t *handle = NULL;
    int start_result;
    int authenticate_result;
    int account_result;

    if (argc != 4) {
        fprintf(stderr, "usage: pam-witness SERVICE USER PASSWORD\n");
        return 2;
    }
    witness_password = argv[3];
    start_result = pam_start(argv[1], argv[2], &conversation, &handle);
    if (start_result != PAM_SUCCESS) {
        fprintf(stderr, "pam-witness: pam_start(%s) failed: %d %s\n", argv[1], start_result,
                pam_strerror(NULL, start_result));
        return 3;
    }
    authenticate_result = pam_authenticate(handle, 0);
    account_result = pam_acct_mgmt(handle, 0);
    printf("authenticate=%d %s\n", authenticate_result, pam_strerror(handle, authenticate_result));
    printf("acct_mgmt=%d %s\n", account_result, pam_strerror(handle, account_result));
    pam_end(handle, authenticate_result);
    if (authenticate_result == PAM_SUCCESS && account_result == PAM_SUCCESS) {
        return 0;
    }
    return 1;
}
