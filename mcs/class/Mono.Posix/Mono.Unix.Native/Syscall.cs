//
// Mono.Unix/Syscall.cs
//
// Authors:
//   Miguel de Icaza (miguel@novell.com)
//   Jonathan Pryor (jonpryor@vt.edu)
//
// (C) 2003 Novell, Inc.
// (C) 2004-2006 Jonathan Pryor
//
// This file implements the low-level syscall interface to the POSIX
// subsystem.
//
// This file tries to stay close to the low-level API as much as possible
// using enumerations, structures and in a few cases, using existing .NET
// data types.
//
// Implementation notes:
//
//    Since the values for the various constants on the API changes
//    from system to system (even Linux on different architectures will
//    have different values), we define our own set of values, and we
//    use a set of C helper routines to map from the constants we define
//    to the values of the native OS.
//
//    Bitfields are flagged with the [Map] attribute, and a helper program
//    generates a set of routines that we can call to convert from our value 
//    definitions to the value definitions expected by the OS; see
//    NativeConvert for the conversion routines.
//
//    Methods that require tuning are bound as `private sys_NAME' methods
//    and then a `NAME' method is exposed.
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Mono.Unix.Native;

namespace Mono.Unix.Native {

	#region Enumerations

	[Flags][Map]
	[CLSCompliant (false)]
	public enum SyslogOptions {
		LOG_PID    = 0x01,  // log the pid with each message
		LOG_CONS   = 0x02,  // log on the console if errors in sending
		LOG_ODELAY = 0x04,  // delay open until first syslog (default)
		LOG_NDELAY = 0x08,  // don't delay open
		LOG_NOWAIT = 0x10,  // don't wait for console forks; DEPRECATED
		LOG_PERROR = 0x20   // log to stderr as well
	}

	[Map]
	[CLSCompliant (false)]
	public enum SyslogFacility {
		LOG_KERN      = 0 << 3,
		LOG_USER      = 1 << 3,
		LOG_MAIL      = 2 << 3,
		LOG_DAEMON    = 3 << 3,
		LOG_AUTH      = 4 << 3,
		LOG_SYSLOG    = 5 << 3,
		LOG_LPR       = 6 << 3,
		LOG_NEWS      = 7 << 3,
		LOG_UUCP      = 8 << 3,
		LOG_CRON      = 9 << 3,
		LOG_AUTHPRIV  = 10 << 3,
		LOG_FTP       = 11 << 3,
		LOG_LOCAL0    = 16 << 3,
		LOG_LOCAL1    = 17 << 3,
		LOG_LOCAL2    = 18 << 3,
		LOG_LOCAL3    = 19 << 3,
		LOG_LOCAL4    = 20 << 3,
		LOG_LOCAL5    = 21 << 3,
		LOG_LOCAL6    = 22 << 3,
		LOG_LOCAL7    = 23 << 3,
	}

	[Map]
	[CLSCompliant (false)]
	public enum SyslogLevel {
		LOG_EMERG   = 0,  // system is unusable
		LOG_ALERT   = 1,  // action must be taken immediately
		LOG_CRIT    = 2,  // critical conditions
		LOG_ERR     = 3,  // warning conditions
		LOG_WARNING = 4,  // warning conditions
		LOG_NOTICE  = 5,  // normal but significant condition
		LOG_INFO    = 6,  // informational
		LOG_DEBUG   = 7   // debug-level messages
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum OpenFlags : int {
		//
		// One of these
		//
		O_RDONLY    = 0x00000000,
		O_WRONLY    = 0x00000001,
		O_RDWR      = 0x00000002,

		//
		// Or-ed with zero or more of these
		//
		O_CREAT     = 0x00000040,
		O_EXCL      = 0x00000080,
		O_NOCTTY    = 0x00000100,
		O_TRUNC     = 0x00000200,
		O_APPEND    = 0x00000400,
		O_NONBLOCK  = 0x00000800,
		O_SYNC      = 0x00001000,

		//
		// These are non-Posix.  Using them will result in errors/exceptions on
		// non-supported platforms.
		//
		// (For example, "C-wrapped" system calls -- calls with implementation in
		// MonoPosixHelper -- will return -1 with errno=EINVAL.  C#-wrapped system
		// calls will generate an exception in NativeConvert, as the value can't be
		// converted on the target platform.)
		//
		
		O_NOFOLLOW  = 0x00020000,
		O_DIRECTORY = 0x00010000,
		O_DIRECT    = 0x00004000,
		O_ASYNC     = 0x00002000,
		O_LARGEFILE = 0x00008000,
		O_CLOEXEC   = 0x00080000,
		O_PATH      = 0x00200000
	}
	
	[Map][Flags]
	[CLSCompliant (false)]
	public enum AtFlags : int {
		AT_SYMLINK_NOFOLLOW = 0x00000100,
		AT_REMOVEDIR        = 0x00000200,
		AT_SYMLINK_FOLLOW   = 0x00000400,
		AT_NO_AUTOMOUNT     = 0x00000800,
		AT_EMPTY_PATH       = 0x00001000
	}
	
	// mode_t
	[Flags][Map]
	[CLSCompliant (false)]
	public enum FilePermissions : uint {
		S_ISUID     = 0x0800, // Set user ID on execution
		S_ISGID     = 0x0400, // Set group ID on execution
		S_ISVTX     = 0x0200, // Save swapped text after use (sticky).
		S_IRUSR     = 0x0100, // Read by owner
		S_IWUSR     = 0x0080, // Write by owner
		S_IXUSR     = 0x0040, // Execute by owner
		S_IRGRP     = 0x0020, // Read by group
		S_IWGRP     = 0x0010, // Write by group
		S_IXGRP     = 0x0008, // Execute by group
		S_IROTH     = 0x0004, // Read by other
		S_IWOTH     = 0x0002, // Write by other
		S_IXOTH     = 0x0001, // Execute by other

		S_IRWXG     = (S_IRGRP | S_IWGRP | S_IXGRP),
		S_IRWXU     = (S_IRUSR | S_IWUSR | S_IXUSR),
		S_IRWXO     = (S_IROTH | S_IWOTH | S_IXOTH),
		ACCESSPERMS = (S_IRWXU | S_IRWXG | S_IRWXO), // 0777
		ALLPERMS    = (S_ISUID | S_ISGID | S_ISVTX | S_IRWXU | S_IRWXG | S_IRWXO), // 07777
		DEFFILEMODE = (S_IRUSR | S_IWUSR | S_IRGRP | S_IWGRP | S_IROTH | S_IWOTH), // 0666

		// Device types
		// Why these are held in "mode_t" is beyond me...
		S_IFMT      = 0xF000, // Bits which determine file type
		[Map(SuppressFlags="S_IFMT")]
		S_IFDIR     = 0x4000, // Directory
		[Map(SuppressFlags="S_IFMT")]
		S_IFCHR     = 0x2000, // Character device
		[Map(SuppressFlags="S_IFMT")]
		S_IFBLK     = 0x6000, // Block device
		[Map(SuppressFlags="S_IFMT")]
		S_IFREG     = 0x8000, // Regular file
		[Map(SuppressFlags="S_IFMT")]
		S_IFIFO     = 0x1000, // FIFO
		[Map(SuppressFlags="S_IFMT")]
		S_IFLNK     = 0xA000, // Symbolic link
		[Map(SuppressFlags="S_IFMT")]
		S_IFSOCK    = 0xC000, // Socket
	}

	[Map]
	[CLSCompliant (false)]
	public enum FcntlCommand : int {
		// Form /usr/include/bits/fcntl.h
		F_DUPFD    =    0, // Duplicate file descriptor.
		F_GETFD    =    1, // Get file descriptor flags.
		F_SETFD    =    2, // Set file descriptor flags.
		F_GETFL    =    3, // Get file status flags.
		F_SETFL    =    4, // Set file status flags.
		F_GETLK    =   12, // Get record locking info. [64]
		F_SETLK    =   13, // Set record locking info (non-blocking). [64]
		F_SETLKW   =   14, // Set record locking info (blocking). [64]
		F_SETOWN   =    8, // Set owner of socket (receiver of SIGIO).
		F_GETOWN   =    9, // Get owner of socket (receiver of SIGIO).
		F_SETSIG   =   10, // Set number of signal to be sent.
		F_GETSIG   =   11, // Get number of signal to be sent.
		F_NOCACHE  =   48, // OSX: turn data caching off/on for this fd.
		F_SETLEASE = 1024, // Set a lease.
		F_GETLEASE = 1025, // Enquire what lease is active.
		F_NOTIFY   = 1026, // Required notifications on a directory
	}

	[Map]
	[CLSCompliant (false)]
	public enum LockType : short {
		F_RDLCK = 0, // Read lock.
		F_WRLCK = 1, // Write lock.
		F_UNLCK = 2, // Remove lock.
	}

	[Map]
	[CLSCompliant (false)]
	public enum SeekFlags : short {
		// values liberally copied from /usr/include/unistd.h
		SEEK_SET = 0, // Seek from beginning of file.
		SEEK_CUR = 1, // Seek from current position.
		SEEK_END = 2, // Seek from end of file.

		L_SET    = SEEK_SET, // BSD alias for SEEK_SET
		L_INCR   = SEEK_CUR, // BSD alias for SEEK_CUR
		L_XTND   = SEEK_END, // BSD alias for SEEK_END
	}
	
	[Map, Flags]
	[CLSCompliant (false)]
	public enum DirectoryNotifyFlags : int {
		// from /usr/include/bits/fcntl.h
		DN_ACCESS    = 0x00000001, // File accessed.
		DN_MODIFY    = 0x00000002, // File modified.
		DN_CREATE    = 0x00000004, // File created.
		DN_DELETE    = 0x00000008, // File removed.
		DN_RENAME    = 0x00000010, // File renamed.
		DN_ATTRIB    = 0x00000020, // File changed attributes.
		DN_MULTISHOT = unchecked ((int)0x80000000), // Don't remove notifier
	}

	[Map]
	[CLSCompliant (false)]
	public enum PosixFadviseAdvice : int {
		POSIX_FADV_NORMAL     = 0,  // No further special treatment.
		POSIX_FADV_RANDOM     = 1,  // Expect random page references.
		POSIX_FADV_SEQUENTIAL = 2,  // Expect sequential page references.
		POSIX_FADV_WILLNEED   = 3,  // Will need these pages.
		POSIX_FADV_DONTNEED   = 4,  // Don't need these pages.
		POSIX_FADV_NOREUSE    = 5,  // Data will be accessed once.
	}

	[Map]
	[CLSCompliant (false)]
	public enum PosixMadviseAdvice : int {
		POSIX_MADV_NORMAL     = 0,  // No further special treatment.
		POSIX_MADV_RANDOM     = 1,  // Expect random page references.
		POSIX_MADV_SEQUENTIAL = 2,  // Expect sequential page references.
		POSIX_MADV_WILLNEED   = 3,  // Will need these pages.
		POSIX_MADV_DONTNEED   = 4,  // Don't need these pages.
	}

	[Map]
	public enum Signum : int {
		SIGHUP    =  1, // Hangup (POSIX).
		SIGINT    =  2, // Interrupt (ANSI).
		SIGQUIT   =  3, // Quit (POSIX).
		SIGILL    =  4, // Illegal instruction (ANSI).
		SIGTRAP   =  5, // Trace trap (POSIX).
		SIGABRT   =  6, // Abort (ANSI).
		SIGIOT    =  6, // IOT trap (4.2 BSD).
		SIGBUS    =  7, // BUS error (4.2 BSD).
		SIGFPE    =  8, // Floating-point exception (ANSI).
		SIGKILL   =  9, // Kill, unblockable (POSIX).
		SIGUSR1   = 10, // User-defined signal 1 (POSIX).
		SIGSEGV   = 11, // Segmentation violation (ANSI).
		SIGUSR2   = 12, // User-defined signal 2 (POSIX).
		SIGPIPE   = 13, // Broken pipe (POSIX).
		SIGALRM   = 14, // Alarm clock (POSIX).
		SIGTERM   = 15, // Termination (ANSI).
		SIGSTKFLT = 16, // Stack fault.
		SIGCLD    = SIGCHLD, // Same as SIGCHLD (System V).
		SIGCHLD   = 17, // Child status has changed (POSIX).
		SIGCONT   = 18, // Continue (POSIX).
		SIGSTOP   = 19, // Stop, unblockable (POSIX).
		SIGTSTP   = 20, // Keyboard stop (POSIX).
		SIGTTIN   = 21, // Background read from tty (POSIX).
		SIGTTOU   = 22, // Background write to tty (POSIX).
		SIGURG    = 23, // Urgent condition on socket (4.2 BSD).
		SIGXCPU   = 24, // CPU limit exceeded (4.2 BSD).
		SIGXFSZ   = 25, // File size limit exceeded (4.2 BSD).
		SIGVTALRM = 26, // Virtual alarm clock (4.2 BSD).
		SIGPROF   = 27, // Profiling alarm clock (4.2 BSD).
		SIGWINCH  = 28, // Window size change (4.3 BSD, Sun).
		SIGPOLL   = SIGIO, // Pollable event occurred (System V).
		SIGIO     = 29, // I/O now possible (4.2 BSD).
		SIGPWR    = 30, // Power failure restart (System V).
		SIGSYS    = 31, // Bad system call.
		SIGUNUSED = 31
	}

	[Flags][Map]
	public enum WaitOptions : int {
		WNOHANG   = 1,  // Don't block waiting
		WUNTRACED = 2,  // Report status of stopped children
	}

  [Flags][Map]
	[CLSCompliant (false)]
	public enum AccessModes : int {
		R_OK = 1,
		W_OK = 2,
		X_OK = 4,
		F_OK = 8,
	}

	[Map]
	[CLSCompliant (false)]
	public enum PathconfName : int {
		_PC_LINK_MAX,
		_PC_MAX_CANON,
		_PC_MAX_INPUT,
		_PC_NAME_MAX,
		_PC_PATH_MAX,
		_PC_PIPE_BUF,
		_PC_CHOWN_RESTRICTED,
		_PC_NO_TRUNC,
		_PC_VDISABLE,
		_PC_SYNC_IO,
		_PC_ASYNC_IO,
		_PC_PRIO_IO,
		_PC_SOCK_MAXBUF,
		_PC_FILESIZEBITS,
		_PC_REC_INCR_XFER_SIZE,
		_PC_REC_MAX_XFER_SIZE,
		_PC_REC_MIN_XFER_SIZE,
		_PC_REC_XFER_ALIGN,
		_PC_ALLOC_SIZE_MIN,
		_PC_SYMLINK_MAX,
		_PC_2_SYMLINKS
	}

	[Map]
	[CLSCompliant (false)]
	public enum SysconfName : int {
		_SC_ARG_MAX,
		_SC_CHILD_MAX,
		_SC_CLK_TCK,
		_SC_NGROUPS_MAX,
		_SC_OPEN_MAX,
		_SC_STREAM_MAX,
		_SC_TZNAME_MAX,
		_SC_JOB_CONTROL,
		_SC_SAVED_IDS,
		_SC_REALTIME_SIGNALS,
		_SC_PRIORITY_SCHEDULING,
		_SC_TIMERS,
		_SC_ASYNCHRONOUS_IO,
		_SC_PRIORITIZED_IO,
		_SC_SYNCHRONIZED_IO,
		_SC_FSYNC,
		_SC_MAPPED_FILES,
		_SC_MEMLOCK,
		_SC_MEMLOCK_RANGE,
		_SC_MEMORY_PROTECTION,
		_SC_MESSAGE_PASSING,
		_SC_SEMAPHORES,
		_SC_SHARED_MEMORY_OBJECTS,
		_SC_AIO_LISTIO_MAX,
		_SC_AIO_MAX,
		_SC_AIO_PRIO_DELTA_MAX,
		_SC_DELAYTIMER_MAX,
		_SC_MQ_OPEN_MAX,
		_SC_MQ_PRIO_MAX,
		_SC_VERSION,
		_SC_PAGESIZE,
		_SC_RTSIG_MAX,
		_SC_SEM_NSEMS_MAX,
		_SC_SEM_VALUE_MAX,
		_SC_SIGQUEUE_MAX,
		_SC_TIMER_MAX,
		/* Values for the argument to `sysconf'
			 corresponding to _POSIX2_* symbols.  */
		_SC_BC_BASE_MAX,
		_SC_BC_DIM_MAX,
		_SC_BC_SCALE_MAX,
		_SC_BC_STRING_MAX,
		_SC_COLL_WEIGHTS_MAX,
		_SC_EQUIV_CLASS_MAX,
		_SC_EXPR_NEST_MAX,
		_SC_LINE_MAX,
		_SC_RE_DUP_MAX,
		_SC_CHARCLASS_NAME_MAX,
		_SC_2_VERSION,
		_SC_2_C_BIND,
		_SC_2_C_DEV,
		_SC_2_FORT_DEV,
		_SC_2_FORT_RUN,
		_SC_2_SW_DEV,
		_SC_2_LOCALEDEF,
		_SC_PII,
		_SC_PII_XTI,
		_SC_PII_SOCKET,
		_SC_PII_INTERNET,
		_SC_PII_OSI,
		_SC_POLL,
		_SC_SELECT,
		_SC_UIO_MAXIOV,
		_SC_IOV_MAX = _SC_UIO_MAXIOV,
		_SC_PII_INTERNET_STREAM,
		_SC_PII_INTERNET_DGRAM,
		_SC_PII_OSI_COTS,
		_SC_PII_OSI_CLTS,
		_SC_PII_OSI_M,
		_SC_T_IOV_MAX,
		/* Values according to POSIX 1003.1c (POSIX threads).  */
		_SC_THREADS,
		_SC_THREAD_SAFE_FUNCTIONS,
		_SC_GETGR_R_SIZE_MAX,
		_SC_GETPW_R_SIZE_MAX,
		_SC_LOGIN_NAME_MAX,
		_SC_TTY_NAME_MAX,
		_SC_THREAD_DESTRUCTOR_ITERATIONS,
		_SC_THREAD_KEYS_MAX,
		_SC_THREAD_STACK_MIN,
		_SC_THREAD_THREADS_MAX,
		_SC_THREAD_ATTR_STACKADDR,
		_SC_THREAD_ATTR_STACKSIZE,
		_SC_THREAD_PRIORITY_SCHEDULING,
		_SC_THREAD_PRIO_INHERIT,
		_SC_THREAD_PRIO_PROTECT,
		_SC_THREAD_PROCESS_SHARED,
		_SC_NPROCESSORS_CONF,
		_SC_NPROCESSORS_ONLN,
		_SC_PHYS_PAGES,
		_SC_AVPHYS_PAGES,
		_SC_ATEXIT_MAX,
		_SC_PASS_MAX,
		_SC_XOPEN_VERSION,
		_SC_XOPEN_XCU_VERSION,
		_SC_XOPEN_UNIX,
		_SC_XOPEN_CRYPT,
		_SC_XOPEN_ENH_I18N,
		_SC_XOPEN_SHM,
		_SC_2_CHAR_TERM,
		_SC_2_C_VERSION,
		_SC_2_UPE,
		_SC_XOPEN_XPG2,
		_SC_XOPEN_XPG3,
		_SC_XOPEN_XPG4,
		_SC_CHAR_BIT,
		_SC_CHAR_MAX,
		_SC_CHAR_MIN,
		_SC_INT_MAX,
		_SC_INT_MIN,
		_SC_LONG_BIT,
		_SC_WORD_BIT,
		_SC_MB_LEN_MAX,
		_SC_NZERO,
		_SC_SSIZE_MAX,
		_SC_SCHAR_MAX,
		_SC_SCHAR_MIN,
		_SC_SHRT_MAX,
		_SC_SHRT_MIN,
		_SC_UCHAR_MAX,
		_SC_UINT_MAX,
		_SC_ULONG_MAX,
		_SC_USHRT_MAX,
		_SC_NL_ARGMAX,
		_SC_NL_LANGMAX,
		_SC_NL_MSGMAX,
		_SC_NL_NMAX,
		_SC_NL_SETMAX,
		_SC_NL_TEXTMAX,
		_SC_XBS5_ILP32_OFF32,
		_SC_XBS5_ILP32_OFFBIG,
		_SC_XBS5_LP64_OFF64,
		_SC_XBS5_LPBIG_OFFBIG,
		_SC_XOPEN_LEGACY,
		_SC_XOPEN_REALTIME,
		_SC_XOPEN_REALTIME_THREADS,
		_SC_ADVISORY_INFO,
		_SC_BARRIERS,
		_SC_BASE,
		_SC_C_LANG_SUPPORT,
		_SC_C_LANG_SUPPORT_R,
		_SC_CLOCK_SELECTION,
		_SC_CPUTIME,
		_SC_THREAD_CPUTIME,
		_SC_DEVICE_IO,
		_SC_DEVICE_SPECIFIC,
		_SC_DEVICE_SPECIFIC_R,
		_SC_FD_MGMT,
		_SC_FIFO,
		_SC_PIPE,
		_SC_FILE_ATTRIBUTES,
		_SC_FILE_LOCKING,
		_SC_FILE_SYSTEM,
		_SC_MONOTONIC_CLOCK,
		_SC_MULTI_PROCESS,
		_SC_SINGLE_PROCESS,
		_SC_NETWORKING,
		_SC_READER_WRITER_LOCKS,
		_SC_SPIN_LOCKS,
		_SC_REGEXP,
		_SC_REGEX_VERSION,
		_SC_SHELL,
		_SC_SIGNALS,
		_SC_SPAWN,
		_SC_SPORADIC_SERVER,
		_SC_THREAD_SPORADIC_SERVER,
		_SC_SYSTEM_DATABASE,
		_SC_SYSTEM_DATABASE_R,
		_SC_TIMEOUTS,
		_SC_TYPED_MEMORY_OBJECTS,
		_SC_USER_GROUPS,
		_SC_USER_GROUPS_R,
		_SC_2_PBS,
		_SC_2_PBS_ACCOUNTING,
		_SC_2_PBS_LOCATE,
		_SC_2_PBS_MESSAGE,
		_SC_2_PBS_TRACK,
		_SC_SYMLOOP_MAX,
		_SC_STREAMS,
		_SC_2_PBS_CHECKPOINT,
		_SC_V6_ILP32_OFF32,
		_SC_V6_ILP32_OFFBIG,
		_SC_V6_LP64_OFF64,
		_SC_V6_LPBIG_OFFBIG,
		_SC_HOST_NAME_MAX,
		_SC_TRACE,
		_SC_TRACE_EVENT_FILTER,
		_SC_TRACE_INHERIT,
		_SC_TRACE_LOG,
		_SC_LEVEL1_ICACHE_SIZE,
		_SC_LEVEL1_ICACHE_ASSOC,
		_SC_LEVEL1_ICACHE_LINESIZE,
		_SC_LEVEL1_DCACHE_SIZE,
		_SC_LEVEL1_DCACHE_ASSOC,
		_SC_LEVEL1_DCACHE_LINESIZE,
		_SC_LEVEL2_CACHE_SIZE,
		_SC_LEVEL2_CACHE_ASSOC,
		_SC_LEVEL2_CACHE_LINESIZE,
		_SC_LEVEL3_CACHE_SIZE,
		_SC_LEVEL3_CACHE_ASSOC,
		_SC_LEVEL3_CACHE_LINESIZE,
		_SC_LEVEL4_CACHE_SIZE,
		_SC_LEVEL4_CACHE_ASSOC,
		_SC_LEVEL4_CACHE_LINESIZE
	}

	[Map]
	[CLSCompliant (false)]
	public enum ConfstrName : int {
		_CS_PATH,			/* The default search path.  */
		_CS_V6_WIDTH_RESTRICTED_ENVS,
		_CS_GNU_LIBC_VERSION,
		_CS_GNU_LIBPTHREAD_VERSION,
		_CS_LFS_CFLAGS = 1000,
		_CS_LFS_LDFLAGS,
		_CS_LFS_LIBS,
		_CS_LFS_LINTFLAGS,
		_CS_LFS64_CFLAGS,
		_CS_LFS64_LDFLAGS,
		_CS_LFS64_LIBS,
		_CS_LFS64_LINTFLAGS,
		_CS_XBS5_ILP32_OFF32_CFLAGS = 1100,
		_CS_XBS5_ILP32_OFF32_LDFLAGS,
		_CS_XBS5_ILP32_OFF32_LIBS,
		_CS_XBS5_ILP32_OFF32_LINTFLAGS,
		_CS_XBS5_ILP32_OFFBIG_CFLAGS,
		_CS_XBS5_ILP32_OFFBIG_LDFLAGS,
		_CS_XBS5_ILP32_OFFBIG_LIBS,
		_CS_XBS5_ILP32_OFFBIG_LINTFLAGS,
		_CS_XBS5_LP64_OFF64_CFLAGS,
		_CS_XBS5_LP64_OFF64_LDFLAGS,
		_CS_XBS5_LP64_OFF64_LIBS,
		_CS_XBS5_LP64_OFF64_LINTFLAGS,
		_CS_XBS5_LPBIG_OFFBIG_CFLAGS,
		_CS_XBS5_LPBIG_OFFBIG_LDFLAGS,
		_CS_XBS5_LPBIG_OFFBIG_LIBS,
		_CS_XBS5_LPBIG_OFFBIG_LINTFLAGS,
		_CS_POSIX_V6_ILP32_OFF32_CFLAGS,
		_CS_POSIX_V6_ILP32_OFF32_LDFLAGS,
		_CS_POSIX_V6_ILP32_OFF32_LIBS,
		_CS_POSIX_V6_ILP32_OFF32_LINTFLAGS,
		_CS_POSIX_V6_ILP32_OFFBIG_CFLAGS,
		_CS_POSIX_V6_ILP32_OFFBIG_LDFLAGS,
		_CS_POSIX_V6_ILP32_OFFBIG_LIBS,
		_CS_POSIX_V6_ILP32_OFFBIG_LINTFLAGS,
		_CS_POSIX_V6_LP64_OFF64_CFLAGS,
		_CS_POSIX_V6_LP64_OFF64_LDFLAGS,
		_CS_POSIX_V6_LP64_OFF64_LIBS,
		_CS_POSIX_V6_LP64_OFF64_LINTFLAGS,
		_CS_POSIX_V6_LPBIG_OFFBIG_CFLAGS,
		_CS_POSIX_V6_LPBIG_OFFBIG_LDFLAGS,
		_CS_POSIX_V6_LPBIG_OFFBIG_LIBS,
		_CS_POSIX_V6_LPBIG_OFFBIG_LINTFLAGS
	}

	[Map]
	[CLSCompliant (false)]
	public enum LockfCommand : int {
		F_ULOCK = 0, // Unlock a previously locked region.
		F_LOCK  = 1, // Lock a region for exclusive use.
		F_TLOCK = 2, // Test and lock a region for exclusive use.
		F_TEST  = 3, // Test a region for other process locks.
	}

	[Map][Flags]
	public enum PollEvents : short {
		POLLIN      = 0x0001, // There is data to read
		POLLPRI     = 0x0002, // There is urgent data to read
		POLLOUT     = 0x0004, // Writing now will not block
		POLLERR     = 0x0008, // Error condition
		POLLHUP     = 0x0010, // Hung up
		POLLNVAL    = 0x0020, // Invalid request; fd not open
		// XPG4.2 definitions (via _XOPEN_SOURCE)
		POLLRDNORM  = 0x0040, // Normal data may be read
		POLLRDBAND  = 0x0080, // Priority data may be read
		POLLWRNORM  = 0x0100, // Writing now will not block
		POLLWRBAND  = 0x0200, // Priority data may be written
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum XattrFlags : int {
		XATTR_AUTO = 0,
		XATTR_CREATE = 1,
		XATTR_REPLACE = 2,
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum MountFlags : ulong {
		ST_RDONLY      =    1,  // Mount read-only
		ST_NOSUID      =    2,  // Ignore suid and sgid bits
		ST_NODEV       =    4,  // Disallow access to device special files
		ST_NOEXEC      =    8,  // Disallow program execution
		ST_SYNCHRONOUS =   16,  // Writes are synced at once
		ST_REMOUNT     =   32,  // Alter flags of a mounted FS
		ST_MANDLOCK    =   64,  // Allow mandatory locks on an FS
		ST_WRITE       =  128,  // Write on file/directory/symlink
		ST_APPEND      =  256,  // Append-only file
		ST_IMMUTABLE   =  512,  // Immutable file
		ST_NOATIME     = 1024,  // Do not update access times
		ST_NODIRATIME  = 2048,  // Do not update directory access times
		ST_BIND        = 4096,  // Bind directory at different place
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum MmapFlags : int {
		MAP_SHARED      = 0x01,     // Share changes.
		MAP_PRIVATE     = 0x02,     // Changes are private.
		MAP_TYPE        = 0x0f,     // Mask for type of mapping.
		MAP_FIXED       = 0x10,     // Interpret addr exactly.
		MAP_FILE        = 0,
		MAP_ANONYMOUS   = 0x20,     // Don't use a file.
		MAP_ANON        = MAP_ANONYMOUS,

		// These are Linux-specific.
		MAP_GROWSDOWN   = 0x00100,  // Stack-like segment.
		MAP_DENYWRITE   = 0x00800,  // ETXTBSY
		MAP_EXECUTABLE  = 0x01000,  // Mark it as an executable.
		MAP_LOCKED      = 0x02000,  // Lock the mapping.
		MAP_NORESERVE   = 0x04000,  // Don't check for reservations.
		MAP_POPULATE    = 0x08000,  // Populate (prefault) pagetables.
		MAP_NONBLOCK    = 0x10000,  // Do not block on IO.
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum MmapProts : int {
		PROT_READ       = 0x1,  // Page can be read.
		PROT_WRITE      = 0x2,  // Page can be written.
		PROT_EXEC       = 0x4,  // Page can be executed.
		PROT_NONE       = 0x0,  // Page can not be accessed.
		PROT_GROWSDOWN  = 0x01000000, // Extend change to start of
		                              //   growsdown vma (mprotect only).
		PROT_GROWSUP    = 0x02000000, // Extend change to start of
		                              //   growsup vma (mprotect only).
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum MsyncFlags : int {
		MS_ASYNC      = 0x1,  // Sync memory asynchronously.
		MS_SYNC       = 0x4,  // Synchronous memory sync.
		MS_INVALIDATE = 0x2,  // Invalidate the caches.
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum MlockallFlags : int {
		MCL_CURRENT	= 0x1,	// Lock all currently mapped pages.
		MCL_FUTURE  = 0x2,	// Lock all additions to address
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum MremapFlags : ulong {
		MREMAP_MAYMOVE = 0x1,
	}

	[Map]
	[CLSCompliant (false)]
	public enum UnixSocketType : int {
		SOCK_STREAM    =  1, // Byte-stream socket
		SOCK_DGRAM     =  2, // Datagram socket
		SOCK_RAW       =  3, // Raw protocol interface (linux specific)
		SOCK_RDM       =  4, // Reliably-delivered messages (linux specific)
		SOCK_SEQPACKET =  5, // Sequenced-packet socket
		SOCK_DCCP      =  6, // Datagram Congestion Control Protocol (linux specific)
		SOCK_PACKET    = 10, // Linux specific
	}

	[Map][Flags]
	[CLSCompliant (false)]
	public enum UnixSocketFlags : int {
		SOCK_CLOEXEC  = 0x80000, /* Atomically set close-on-exec flag for the new descriptor(s). */
		SOCK_NONBLOCK = 0x00800, /* Atomically mark descriptor(s) as non-blocking. */
	}

	[Map]
	[CLSCompliant (false)]
	public enum UnixSocketProtocol : int {
		IPPROTO_ICMP    =    1, /* Internet Control Message Protocol */
		IPPROTO_IGMP    =    2, /* Internet Group Management Protocol */
		IPPROTO_IPIP    =    4, /* IPIP tunnels (older KA9Q tunnels use 94) */
		IPPROTO_TCP     =    6, /* Transmission Control Protocol */
		IPPROTO_EGP     =    8, /* Exterior Gateway Protocol */
		IPPROTO_PUP     =   12, /* PUP protocol */
		IPPROTO_UDP     =   17, /* User Datagram Protocol */
		IPPROTO_IDP     =   22, /* XNS IDP protocol */
		IPPROTO_TP      =   29, /* SO Transport Protocol Class 4 */
		IPPROTO_DCCP    =   33, /* Datagram Congestion Control Protocol */
		IPPROTO_IPV6    =   41, /* IPv6-in-IPv4 tunnelling */
		IPPROTO_RSVP    =   46, /* RSVP Protocol */
		IPPROTO_GRE     =   47, /* Cisco GRE tunnels (rfc 1701,1702) */
		IPPROTO_ESP     =   50, /* Encapsulation Security Payload protocol */
		IPPROTO_AH      =   51, /* Authentication Header protocol */
		IPPROTO_MTP     =   92, /* Multicast Transport Protocol */
		IPPROTO_BEETPH  =   94, /* IP option pseudo header for BEET */
		IPPROTO_ENCAP   =   98, /* Encapsulation Header */
		IPPROTO_PIM     =  103, /* Protocol Independent Multicast */
		IPPROTO_COMP    =  108, /* Compression Header Protocol */
		IPPROTO_SCTP    =  132, /* Stream Control Transport Protocol */
		IPPROTO_UDPLITE =  136, /* UDP-Lite (RFC 3828) */
		IPPROTO_RAW     =  255, /* Raw IP packets */

		// Number used by linux (0) has a special meaning for socket()
		IPPROTO_IP      = 1024, /* Dummy protocol for TCP */
		// Number used by linux (1) clashes with IPPROTO_ICMP
		SOL_SOCKET      = 2048, /* For setsockopt() / getsockopt(): Options to be accessed at socket level, not protocol level. */
	}

	[Map]
	[CLSCompliant (false)]
	public enum UnixAddressFamily : int {
		AF_UNSPEC     =  0,  /* Unspecified. */
		AF_UNIX       =  1,  /* Local to host (pipes and file-domain). */
		AF_INET       =  2,  /* IP protocol family. */
		AF_AX25       =  3,  /* Amateur Radio AX.25. */
		AF_IPX        =  4,  /* Novell Internet Protocol. */
		AF_APPLETALK  =  5,  /* Appletalk DDP. */
		AF_NETROM     =  6,  /* Amateur radio NetROM. */
		AF_BRIDGE     =  7,  /* Multiprotocol bridge. */
		AF_ATMPVC     =  8,  /* ATM PVCs. */
		AF_X25        =  9,  /* Reserved for X.25 project. */
		AF_INET6      = 10,  /* IP version 6. */
		AF_ROSE       = 11,  /* Amateur Radio X.25 PLP. */
		AF_DECnet     = 12,  /* Reserved for DECnet project. */
		AF_NETBEUI    = 13,  /* Reserved for 802.2LLC project. */
		AF_SECURITY   = 14,  /* Security callback pseudo AF. */
		AF_KEY        = 15,  /* PF_KEY key management API. */
		AF_NETLINK    = 16,
		AF_PACKET     = 17,  /* Packet family. */
		AF_ASH        = 18,  /* Ash. */
		AF_ECONET     = 19,  /* Acorn Econet. */
		AF_ATMSVC     = 20,  /* ATM SVCs. */
		AF_RDS        = 21,  /* RDS sockets. */
		AF_SNA        = 22,  /* Linux SNA Project */
		AF_IRDA       = 23,  /* IRDA sockets. */
		AF_PPPOX      = 24,  /* PPPoX sockets. */
		AF_WANPIPE    = 25,  /* Wanpipe API sockets. */
		AF_LLC        = 26,  /* Linux LLC. */
		AF_CAN        = 29,  /* Controller Area Network. */
		AF_TIPC       = 30,  /* TIPC sockets. */
		AF_BLUETOOTH  = 31,  /* Bluetooth sockets. */
		AF_IUCV       = 32,  /* IUCV sockets. */
		AF_RXRPC      = 33,  /* RxRPC sockets. */
		AF_ISDN       = 34,  /* mISDN sockets. */
		AF_PHONET     = 35,  /* Phonet sockets. */
		AF_IEEE802154 = 36,  /* IEEE 802.15.4 sockets. */
		AF_CAIF       = 37,  /* CAIF sockets. */
		AF_ALG        = 38,  /* Algorithm sockets. */
		AF_NFC        = 39,  /* NFC sockets. */
		AF_VSOCK      = 40,  /* vSockets. */

		// Value used when a syscall returns an unknown address family value
		Unknown       = 65536,
	}

	[Map]
	[CLSCompliant (false)]
	public enum UnixSocketOptionName : int {
		SO_DEBUG                         =  1,
		SO_REUSEADDR                     =  2,
		SO_TYPE                          =  3,
		SO_ERROR                         =  4,
		SO_DONTROUTE                     =  5,
		SO_BROADCAST                     =  6,
		SO_SNDBUF                        =  7,
		SO_RCVBUF                        =  8,
		SO_SNDBUFFORCE                   = 32,
		SO_RCVBUFFORCE                   = 33,
		SO_KEEPALIVE                     =  9,
		SO_OOBINLINE                     = 10,
		SO_NO_CHECK                      = 11,
		SO_PRIORITY                      = 12,
		SO_LINGER                        = 13,
		SO_BSDCOMPAT                     = 14,
		SO_REUSEPORT                     = 15,
		SO_PASSCRED                      = 16,
		SO_PEERCRED                      = 17,
		SO_RCVLOWAT                      = 18,
		SO_SNDLOWAT                      = 19,
		SO_RCVTIMEO                      = 20,
		SO_SNDTIMEO                      = 21,
		SO_SECURITY_AUTHENTICATION       = 22,
		SO_SECURITY_ENCRYPTION_TRANSPORT = 23,
		SO_SECURITY_ENCRYPTION_NETWORK   = 24,
		SO_BINDTODEVICE                  = 25,
		SO_ATTACH_FILTER                 = 26,
		SO_DETACH_FILTER                 = 27,
		SO_PEERNAME                      = 28,
		SO_TIMESTAMP                     = 29,
		SO_ACCEPTCONN                    = 30,
		SO_PEERSEC                       = 31,
		SO_PASSSEC                       = 34,
		SO_TIMESTAMPNS                   = 35,
		SO_MARK                          = 36,
		SO_TIMESTAMPING                  = 37,
		SO_PROTOCOL                      = 38,
		SO_DOMAIN                        = 39,
		SO_RXQ_OVFL                      = 40,
		SO_WIFI_STATUS                   = 41,
		SO_PEEK_OFF                      = 42,
		SO_NOFCS                         = 43,
		SO_LOCK_FILTER                   = 44,
		SO_SELECT_ERR_QUEUE              = 45,
		SO_BUSY_POLL                     = 46,
		SO_MAX_PACING_RATE               = 47,
	}

	[Flags][Map]
	[CLSCompliant (false)]
	public enum MessageFlags : int {
		MSG_OOB          =       0x01, /* Process out-of-band data. */
		MSG_PEEK         =       0x02, /* Peek at incoming messages. */
		MSG_DONTROUTE    =       0x04, /* Don't use local routing. */
		MSG_CTRUNC       =       0x08, /* Control data lost before delivery. */
		MSG_PROXY        =       0x10, /* Supply or ask second address. */
		MSG_TRUNC        =       0x20,
		MSG_DONTWAIT     =       0x40, /* Nonblocking IO. */
		MSG_EOR          =       0x80, /* End of record. */
		MSG_WAITALL      =      0x100, /* Wait for a full request. */
		MSG_FIN          =      0x200,
		MSG_SYN          =      0x400,
		MSG_CONFIRM      =      0x800, /* Confirm path validity. */
		MSG_RST          =     0x1000,
		MSG_ERRQUEUE     =     0x2000, /* Fetch message from error queue. */
		MSG_NOSIGNAL     =     0x4000, /* Do not generate SIGPIPE. */
		MSG_MORE         =     0x8000, /* Sender will send more. */
		MSG_WAITFORONE   =    0x10000, /* Wait for at least one packet to return.*/
		MSG_FASTOPEN     = 0x20000000, /* Send data in TCP SYN. */
		MSG_CMSG_CLOEXEC = 0x40000000, /* Set close_on_exit for file descriptor received through SCM_RIGHTS. */
	}

	[Map]
	[CLSCompliant (false)]
	public enum ShutdownOption : int {
		SHUT_RD   = 0x01,   /* No more receptions. */
		SHUT_WR   = 0x02,   /* No more transmissions. */
		SHUT_RDWR = 0x03,   /* No more receptions or transmissions. */
	}

	// Used by libMonoPosixHelper to distinguish between different sockaddr types
	[Map]
	enum SockaddrType : int {
		Invalid,
		SockaddrStorage,
		SockaddrUn,
		Sockaddr,
		SockaddrIn,
		SockaddrIn6,

		// Flag to indicate that this Sockaddr must be wrapped with a _SockaddrDynamic wrapper
		MustBeWrapped = 0x8000,
	}

	[Map]
	[CLSCompliant (false)]
	public enum UnixSocketControlMessage : int {
		SCM_RIGHTS      = 0x01,  /* Transfer file descriptors. */
		SCM_CREDENTIALS = 0x02,  /* Credentials passing. */
	}

	#endregion

	#region Structures

	[Map ("struct flock")]
	public struct Flock
		: IEquatable <Flock>
	{
		[CLSCompliant (false)]
		public LockType         l_type;    // Type of lock: F_RDLCK, F_WRLCK, F_UNLCK
		[CLSCompliant (false)]
		public SeekFlags        l_whence;  // How to interpret l_start
		[off_t] public long     l_start;   // Starting offset for lock
		[off_t] public long     l_len;     // Number of bytes to lock
		[pid_t] public int      l_pid;     // PID of process blocking our lock (F_GETLK only)

		public override int GetHashCode ()
		{
			return l_type.GetHashCode () ^ l_whence.GetHashCode () ^ 
				l_start.GetHashCode () ^ l_len.GetHashCode () ^
				l_pid.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if ((obj == null) || (obj.GetType () != GetType ()))
				return false;
			Flock value = (Flock) obj;
			return l_type == value.l_type && l_whence == value.l_whence && 
				l_start == value.l_start && l_len == value.l_len && 
				l_pid == value.l_pid;
		}

		public bool Equals (Flock value)
		{
			return l_type == value.l_type && l_whence == value.l_whence && 
				l_start == value.l_start && l_len == value.l_len && 
				l_pid == value.l_pid;
		}

		public static bool operator== (Flock lhs, Flock rhs)
		{
			return lhs.Equals (rhs);
		}

		public static bool operator!= (Flock lhs, Flock rhs)
		{
			return !lhs.Equals (rhs);
		}
	}

	[Map ("struct pollfd")]
	public struct Pollfd
		: IEquatable <Pollfd>
	{
		public int fd;
		[CLSCompliant (false)]
		public PollEvents events;
		[CLSCompliant (false)]
		public PollEvents revents;

		public override int GetHashCode ()
		{
			return events.GetHashCode () ^ revents.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || obj.GetType () != GetType ())
				return false;
			Pollfd value = (Pollfd) obj;
			return value.events == events && value.revents == revents;
		}

		public bool Equals (Pollfd value)
		{
			return value.events == events && value.revents == revents;
		}

		public static bool operator== (Pollfd lhs, Pollfd rhs)
		{
			return lhs.Equals (rhs);
		}

		public static bool operator!= (Pollfd lhs, Pollfd rhs)
		{
			return !lhs.Equals (rhs);
		}
	}

	// Use manually written To/From methods to handle fields st_atime_nsec etc.
	public struct Stat
		: IEquatable <Stat>
	{
		[CLSCompliant (false)]
		[dev_t]     public ulong    st_dev;     // device
		[CLSCompliant (false)]
		[ino_t]     public  ulong   st_ino;     // inode
		[CLSCompliant (false)]
		public  FilePermissions     st_mode;    // protection
		[NonSerialized]
#pragma warning disable 169		
		private uint                _padding_;  // padding for structure alignment
#pragma warning restore 169		
		[CLSCompliant (false)]
		[nlink_t]   public  ulong   st_nlink;   // number of hard links
		[CLSCompliant (false)]
		[uid_t]     public  uint    st_uid;     // user ID of owner
		[CLSCompliant (false)]
		[gid_t]     public  uint    st_gid;     // group ID of owner
		[CLSCompliant (false)]
		[dev_t]     public  ulong   st_rdev;    // device type (if inode device)
		[off_t]     public  long    st_size;    // total size, in bytes
		[blksize_t] public  long    st_blksize; // blocksize for filesystem I/O
		[blkcnt_t]  public  long    st_blocks;  // number of blocks allocated
		[time_t]    public  long    st_atime;   // time of last access
		[time_t]    public  long    st_mtime;   // time of last modification
		[time_t]    public  long    st_ctime;   // time of last status change
		public  long             st_atime_nsec; // Timespec.tv_nsec partner to st_atime
		public  long             st_mtime_nsec; // Timespec.tv_nsec partner to st_mtime
		public  long             st_ctime_nsec; // Timespec.tv_nsec partner to st_ctime

		public Timespec st_atim {
			get {
				return new Timespec { tv_sec = st_atime, tv_nsec = st_atime_nsec };
			}
			set {
				st_atime = value.tv_sec;
				st_atime_nsec = value.tv_nsec;
			}
		}

		public Timespec st_mtim {
			get {
				return new Timespec { tv_sec = st_mtime, tv_nsec = st_mtime_nsec };
			}
			set {
				st_mtime = value.tv_sec;
				st_mtime_nsec = value.tv_nsec;
			}
		}

		public Timespec st_ctim {
			get {
				return new Timespec { tv_sec = st_ctime, tv_nsec = st_ctime_nsec };
			}
			set {
				st_ctime = value.tv_sec;
				st_ctime_nsec = value.tv_nsec;
			}
		}

		public override int GetHashCode ()
		{
			return st_dev.GetHashCode () ^
				st_ino.GetHashCode () ^
				st_mode.GetHashCode () ^
				st_nlink.GetHashCode () ^
				st_uid.GetHashCode () ^
				st_gid.GetHashCode () ^
				st_rdev.GetHashCode () ^
				st_size.GetHashCode () ^
				st_blksize.GetHashCode () ^
				st_blocks.GetHashCode () ^
				st_atime.GetHashCode () ^
				st_mtime.GetHashCode () ^
				st_ctime.GetHashCode () ^
				st_atime_nsec.GetHashCode () ^
				st_mtime_nsec.GetHashCode () ^
				st_ctime_nsec.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || obj.GetType() != GetType ())
				return false;
			Stat value = (Stat) obj;
			return value.st_dev == st_dev &&
				value.st_ino == st_ino &&
				value.st_mode == st_mode &&
				value.st_nlink == st_nlink &&
				value.st_uid == st_uid &&
				value.st_gid == st_gid &&
				value.st_rdev == st_rdev &&
				value.st_size == st_size &&
				value.st_blksize == st_blksize &&
				value.st_blocks == st_blocks &&
				value.st_atime == st_atime &&
				value.st_mtime == st_mtime &&
				value.st_ctime == st_ctime &&
				value.st_atime_nsec == st_atime_nsec &&
				value.st_mtime_nsec == st_mtime_nsec &&
				value.st_ctime_nsec == st_ctime_nsec;
		}

		public bool Equals (Stat value)
		{
			return value.st_dev == st_dev &&
				value.st_ino == st_ino &&
				value.st_mode == st_mode &&
				value.st_nlink == st_nlink &&
				value.st_uid == st_uid &&
				value.st_gid == st_gid &&
				value.st_rdev == st_rdev &&
				value.st_size == st_size &&
				value.st_blksize == st_blksize &&
				value.st_blocks == st_blocks &&
				value.st_atime == st_atime &&
				value.st_mtime == st_mtime &&
				value.st_ctime == st_ctime &&
				value.st_atime_nsec == st_atime_nsec &&
				value.st_mtime_nsec == st_mtime_nsec &&
				value.st_ctime_nsec == st_ctime_nsec;
		}

		public static bool operator== (Stat lhs, Stat rhs)
		{
			return lhs.Equals (rhs);
		}

		public static bool operator!= (Stat lhs, Stat rhs)
		{
			return !lhs.Equals (rhs);
		}
	}

	// `struct statvfs' isn't portable, so don't generate To/From methods.
	[Map]
	[CLSCompliant (false)]
	public struct Statvfs
		: IEquatable <Statvfs>
	{
		public                  ulong f_bsize;	  // file system block size
		public                  ulong f_frsize;   // fragment size
		[fsblkcnt_t] public     ulong f_blocks;   // size of fs in f_frsize units
		[fsblkcnt_t] public     ulong f_bfree;    // # free blocks
		[fsblkcnt_t] public     ulong f_bavail;   // # free blocks for non-root
		[fsfilcnt_t] public     ulong f_files;    // # inodes
		[fsfilcnt_t] public     ulong f_ffree;    // # free inodes
		[fsfilcnt_t] public     ulong f_favail;   // # free inodes for non-root
		public                  ulong f_fsid;     // file system id
		public MountFlags             f_flag;     // mount flags
		public                  ulong f_namemax;  // maximum filename length

		public override int GetHashCode ()
		{
			return f_bsize.GetHashCode () ^
				f_frsize.GetHashCode () ^
				f_blocks.GetHashCode () ^
				f_bfree.GetHashCode () ^
				f_bavail.GetHashCode () ^
				f_files.GetHashCode () ^
				f_ffree.GetHashCode () ^
				f_favail.GetHashCode () ^
				f_fsid.GetHashCode () ^
				f_flag.GetHashCode () ^
				f_namemax.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || obj.GetType() != GetType ())
				return false;
			Statvfs value = (Statvfs) obj;
			return value.f_bsize == f_bsize &&
				value.f_frsize == f_frsize &&
				value.f_blocks == f_blocks &&
				value.f_bfree == f_bfree &&
				value.f_bavail == f_bavail &&
				value.f_files == f_files &&
				value.f_ffree == f_ffree &&
				value.f_favail == f_favail &&
				value.f_fsid == f_fsid &&
				value.f_flag == f_flag &&
				value.f_namemax == f_namemax;
		}

		public bool Equals (Statvfs value)
		{
			return value.f_bsize == f_bsize &&
				value.f_frsize == f_frsize &&
				value.f_blocks == f_blocks &&
				value.f_bfree == f_bfree &&
				value.f_bavail == f_bavail &&
				value.f_files == f_files &&
				value.f_ffree == f_ffree &&
				value.f_favail == f_favail &&
				value.f_fsid == f_fsid &&
				value.f_flag == f_flag &&
				value.f_namemax == f_namemax;
		}

		public static bool operator== (Statvfs lhs, Statvfs rhs)
		{
			return lhs.Equals (rhs);
		}

		public static bool operator!= (Statvfs lhs, Statvfs rhs)
		{
			return !lhs.Equals (rhs);
		}
	}

	[Map ("struct timeval")]
	public struct Timeval
		: IEquatable <Timeval>
	{
		[time_t]      public long tv_sec;   // seconds
		[suseconds_t] public long tv_usec;  // microseconds

		public override int GetHashCode ()
		{
			return tv_sec.GetHashCode () ^ tv_usec.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || obj.GetType () != GetType ())
				return false;
			Timeval value = (Timeval) obj;
			return value.tv_sec == tv_sec && value.tv_usec == tv_usec;
		}

		public bool Equals (Timeval value)
		{
			return value.tv_sec == tv_sec && value.tv_usec == tv_usec;
		}

		public static bool operator== (Timeval lhs, Timeval rhs)
		{
			return lhs.Equals (rhs);
		}

		public static bool operator!= (Timeval lhs, Timeval rhs)
		{
			return !lhs.Equals (rhs);
		}
	}

	[Map ("struct timezone")]
	public struct Timezone
		: IEquatable <Timezone>
	{
		public  int tz_minuteswest; // minutes W of Greenwich
#pragma warning disable 169		
		private int tz_dsttime;     // type of dst correction (OBSOLETE)
#pragma warning restore 169		

		public override int GetHashCode ()
		{
			return tz_minuteswest.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || obj.GetType () != GetType ())
				return false;
			Timezone value = (Timezone) obj;
			return value.tz_minuteswest == tz_minuteswest;
		}

		public bool Equals (Timezone value)
		{
			return value.tz_minuteswest == tz_minuteswest;
		}

		public static bool operator== (Timezone lhs, Timezone rhs)
		{
			return lhs.Equals (rhs);
		}

		public static bool operator!= (Timezone lhs, Timezone rhs)
		{
			return !lhs.Equals (rhs);
		}
	}

	[Map ("struct utimbuf")]
	public struct Utimbuf
		: IEquatable <Utimbuf>
	{
		[time_t] public long    actime;   // access time
		[time_t] public long    modtime;  // modification time

		public override int GetHashCode ()
		{
			return actime.GetHashCode () ^ modtime.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || obj.GetType () != GetType ())
				return false;
			Utimbuf value = (Utimbuf) obj;
			return value.actime == actime && value.modtime == modtime;
		}

		public bool Equals (Utimbuf value)
		{
			return value.actime == actime && value.modtime == modtime;
		}

		public static bool operator== (Utimbuf lhs, Utimbuf rhs)
		{
			return lhs.Equals (rhs);
		}

		public static bool operator!= (Utimbuf lhs, Utimbuf rhs)
		{
			return !lhs.Equals (rhs);
		}
	}

	[Map ("struct timespec")]
	public struct Timespec
		: IEquatable <Timespec>
	{
		[time_t] public long    tv_sec;   // Seconds.
		public          long    tv_nsec;  // Nanoseconds.

		public override int GetHashCode ()
		{
			return tv_sec.GetHashCode () ^ tv_nsec.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || obj.GetType () != GetType ())
				return false;
			Timespec value = (Timespec) obj;
			return value.tv_sec == tv_sec && value.tv_nsec == tv_nsec;
		}

		public bool Equals (Timespec value)
		{
			return value.tv_sec == tv_sec && value.tv_nsec == tv_nsec;
		}

		public static bool operator== (Timespec lhs, Timespec rhs)
		{
			return lhs.Equals (rhs);
		}

		public static bool operator!= (Timespec lhs, Timespec rhs)
		{
			return !lhs.Equals (rhs);
		}
	}

	[Map ("struct iovec")]
	public struct Iovec
	{
		public IntPtr   iov_base; // Starting address
		[CLSCompliant (false)]
		public ulong    iov_len;  // Number of bytes to transfer
	}

	[Flags][Map]
	public enum EpollFlags {
		EPOLL_CLOEXEC = 02000000,
		EPOLL_NONBLOCK = 04000,
	}

	[Flags][Map]
	[CLSCompliant (false)]
	public enum EpollEvents : uint {
		EPOLLIN = 0x001,
		EPOLLPRI = 0x002,
		EPOLLOUT = 0x004,
		EPOLLRDNORM = 0x040,
		EPOLLRDBAND = 0x080,
		EPOLLWRNORM = 0x100,
		EPOLLWRBAND = 0x200,
		EPOLLMSG = 0x400,
		EPOLLERR = 0x008,
		EPOLLHUP = 0x010,
		EPOLLRDHUP = 0x2000,
		EPOLLONESHOT = 1 << 30,
		EPOLLET = unchecked ((uint) (1 << 31))
	}

	public enum EpollOp {
		EPOLL_CTL_ADD = 1,
		EPOLL_CTL_DEL = 2,
		EPOLL_CTL_MOD = 3,
	}

	[StructLayout (LayoutKind.Explicit, Size=12, Pack=1)]
	[CLSCompliant (false)]
	public struct EpollEvent {
		[FieldOffset (0)]
		public EpollEvents events;
		[FieldOffset (4)]
		public int fd;
		[FieldOffset (4)]
		public IntPtr ptr;
		[FieldOffset (4)]
		public uint u32;
		[FieldOffset (4)]
		public ulong u64;
	}

	[Map ("struct linger")]
	[CLSCompliant (false)]
	public struct Linger {
		public int l_onoff;
		public int l_linger;

		public override string ToString ()
		{
			return string.Format ("{0}, {1}", l_onoff, l_linger);
		}
	}

	[Map]
	[StructLayout (LayoutKind.Sequential)]
	[CLSCompliant (false)]
	public struct InAddr : IEquatable<InAddr> {
		public uint s_addr;

		public unsafe InAddr (byte b0, byte b1, byte b2, byte b3)
		{
			s_addr = 0;
			fixed (uint* ptr = &s_addr) {
				byte* bytePtr = (byte*) ptr;
				bytePtr[0] = b0;
				bytePtr[1] = b1;
				bytePtr[2] = b2;
				bytePtr[3] = b3;
			}
		}

		public unsafe InAddr (byte[] buffer)
		{
			if (buffer.Length != 4)
				throw new ArgumentException ("buffer.Length != 4", "buffer");
			s_addr = 0;
			fixed (uint* ptr = &s_addr)
				Marshal.Copy (buffer, 0, (IntPtr) ptr, 4);
		}

		public unsafe void CopyFrom (byte[] source, int startIndex)
		{
			fixed (uint* ptr = &s_addr)
				Marshal.Copy (source, startIndex, (IntPtr) ptr, 4);
		}

		public unsafe void CopyTo (byte[] destination, int startIndex)
		{
			fixed (uint* ptr = &s_addr)
				Marshal.Copy ((IntPtr) ptr, destination, startIndex, 4);
		}

		public unsafe byte this[int index] {
			get {
				if (index < 0 || index >= 4)
					throw new ArgumentOutOfRangeException ("index", "index < 0 || index >= 4");
				fixed (uint* ptr = &s_addr)
					return ((byte*) ptr)[index];
			}
			set {
				if (index < 0 || index >= 4)
					throw new ArgumentOutOfRangeException ("index", "index < 0 || index >= 4");
				fixed (uint* ptr = &s_addr)
					((byte*) ptr)[index] = value;
			}
		}

		public override string ToString ()
		{
			return NativeConvert.ToIPAddress (this).ToString ();
		}

		public override int GetHashCode ()
		{
			return s_addr.GetHashCode ();
		}
		public override bool Equals (object obj)
		{
			if (!(obj is InAddr))
				return false;
			return Equals ((InAddr) obj);
		}
		public bool Equals (InAddr value)
		{
			return s_addr == value.s_addr;
		}
	}

	[Map]
	[StructLayout (LayoutKind.Sequential)]
	public struct In6Addr : IEquatable<In6Addr> {
		ulong addr0;
		ulong addr1;

		public unsafe In6Addr (byte[] buffer)
		{
			if (buffer.Length != 16)
				throw new ArgumentException ("buffer.Length != 16", "buffer");
			addr0 = addr1 = 0;
			fixed (ulong* ptr = &addr0)
				Marshal.Copy (buffer, 0, (IntPtr) ptr, 16);
		}

		public unsafe void CopyFrom (byte[] source, int startIndex)
		{
			fixed (ulong* ptr = &addr0)
				Marshal.Copy (source, startIndex, (IntPtr) ptr, 16);
		}

		public unsafe void CopyTo (byte[] destination, int startIndex)
		{
			fixed (ulong* ptr = &addr0)
				Marshal.Copy ((IntPtr) ptr, destination, startIndex, 16);
		}

		public unsafe byte this[int index] {
			get {
				if (index < 0 || index >= 16)
					throw new ArgumentOutOfRangeException ("index", "index < 0 || index >= 16");
				fixed (ulong* ptr = &addr0)
					return ((byte*) ptr)[index];
			}
			set {
				if (index < 0 || index >= 16)
					throw new ArgumentOutOfRangeException ("index", "index < 0 || index >= 16");
				fixed (ulong* ptr = &addr0)
					((byte*) ptr)[index] = value;
			}
		}

		public override string ToString ()
		{
			return NativeConvert.ToIPAddress (this).ToString ();
		}

		public override int GetHashCode ()
		{
			return addr0.GetHashCode () ^ addr1.GetHashCode ();
		}
		public override bool Equals (object obj)
		{
			if (!(obj is In6Addr))
				return false;
			return Equals ((In6Addr) obj);
		}
		public bool Equals (In6Addr value)
		{
			return addr0 == value.addr0 && addr1 == value.addr1;
		}
	}

	[Map ("struct cmsghdr")]
	[CLSCompliant (false)]
	public struct Cmsghdr {
		public long cmsg_len;
		public UnixSocketProtocol cmsg_level;
		public UnixSocketControlMessage cmsg_type;

		[DllImport (Syscall.MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Cmsghdr_getsize")]
		static extern int getsize ();
		static readonly int size = getsize ();
		public static int Size {
			get {
				return size;
			}
		}

		// Read a struct cmsghdr from msgh.msg_control at offset cmsg and convert it to managed Cmsghdr structure
		public static unsafe Cmsghdr ReadFromBuffer (Msghdr msgh, long cmsg)
		{
			if (msgh == null)
				throw new ArgumentNullException ("msgh");
			if (msgh.msg_control == null || msgh.msg_controllen > msgh.msg_control.Length)
				throw new ArgumentException ("msgh.msg_control == null || msgh.msg_controllen > msgh.msg_control.Length", "msgh");
			if (cmsg < 0 || cmsg + Cmsghdr.Size > msgh.msg_controllen)
				throw new ArgumentException ("cmsg offset pointing out of buffer", "cmsg");

			Cmsghdr hdr;
			fixed (byte* ptr = msgh.msg_control)
				if (!NativeConvert.TryCopy ((IntPtr) (ptr + cmsg), out hdr))
					throw new ArgumentException ("Failed to convert from native struct", "buffer");
			// SOL_SOCKET has the same value as IPPROTO_ICMP on linux.
			// Make sure that cmsg_level is set to SOL_SOCKET in this case.
			if (NativeConvert.FromUnixSocketProtocol (hdr.cmsg_level) == NativeConvert.FromUnixSocketProtocol (UnixSocketProtocol.SOL_SOCKET))
				hdr.cmsg_level = UnixSocketProtocol.SOL_SOCKET;
			return hdr;
		}

		// Convert the Cmsghdr to a native struct cmsghdr and write it to msgh.msg_control at offset cmsg
		public unsafe void WriteToBuffer (Msghdr msgh, long cmsg)
		{
			if (msgh == null)
				throw new ArgumentNullException ("msgh");
			if (msgh.msg_control == null || msgh.msg_controllen > msgh.msg_control.Length)
				throw new ArgumentException ("msgh.msg_control == null || msgh.msg_controllen > msgh.msg_control.Length", "msgh");
			if (cmsg < 0 || cmsg + Cmsghdr.Size > msgh.msg_controllen)
				throw new ArgumentException ("cmsg offset pointing out of buffer", "cmsg");

			fixed (byte* ptr = msgh.msg_control)
				if (!NativeConvert.TryCopy (ref this, (IntPtr) (ptr + cmsg)))
					throw new ArgumentException ("Failed to convert to native struct", "buffer");
		}
	}

	#endregion

	#region Classes

	public sealed class Dirent
		: IEquatable <Dirent>
	{
		[CLSCompliant (false)]
		public /* ino_t */ ulong  d_ino;
		public /* off_t */ long   d_off;
		[CLSCompliant (false)]
		public ushort             d_reclen;
		public byte               d_type;
		public string             d_name;

		public override int GetHashCode ()
		{
			return d_ino.GetHashCode () ^ d_off.GetHashCode () ^ 
				d_reclen.GetHashCode () ^ d_type.GetHashCode () ^
				d_name.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || GetType() != obj.GetType())
				return false;
			Dirent d = (Dirent) obj;
			return Equals (d);
		}

		public bool Equals (Dirent value)
		{
			if (value == null)
				return false;
			return value.d_ino == d_ino && value.d_off == d_off &&
				value.d_reclen == d_reclen && value.d_type == d_type &&
				value.d_name == d_name;
		}

		public override string ToString ()
		{
			return d_name;
		}

		public static bool operator== (Dirent lhs, Dirent rhs)
		{
			return Object.Equals (lhs, rhs);
		}

		public static bool operator!= (Dirent lhs, Dirent rhs)
		{
			return !Object.Equals (lhs, rhs);
		}
	}

	public sealed class Fstab
		: IEquatable <Fstab>
	{
		public string fs_spec;
		public string fs_file;
		public string fs_vfstype;
		public string fs_mntops;
		public string fs_type;
		public int    fs_freq;
		public int    fs_passno;

		public override int GetHashCode ()
		{
			return fs_spec.GetHashCode () ^ fs_file.GetHashCode () ^
				fs_vfstype.GetHashCode () ^ fs_mntops.GetHashCode () ^
				fs_type.GetHashCode () ^ fs_freq ^ fs_passno;
		}

		public override bool Equals (object obj)
		{
			if (obj == null || GetType() != obj.GetType())
				return false;
			Fstab f = (Fstab) obj;
			return Equals (f);
		}

		public bool Equals (Fstab value)
		{
			if (value == null)
				return false;
			return value.fs_spec == fs_spec && value.fs_file == fs_file &&
				value.fs_vfstype == fs_vfstype && value.fs_mntops == fs_mntops &&
				value.fs_type == fs_type && value.fs_freq == fs_freq && 
				value.fs_passno == fs_passno;
		}

		public override string ToString ()
		{
			return fs_spec;
		}

		public static bool operator== (Fstab lhs, Fstab rhs)
		{
			return Object.Equals (lhs, rhs);
		}

		public static bool operator!= (Fstab lhs, Fstab rhs)
		{
			return !Object.Equals (lhs, rhs);
		}
	}

	public sealed class Group
		: IEquatable <Group>
	{
		public string           gr_name;
		public string           gr_passwd;
		[CLSCompliant (false)]
		public /* gid_t */ uint gr_gid;
		public string[]         gr_mem;

		public override int GetHashCode ()
		{
			int memhc = 0;
			for (int i = 0; i < gr_mem.Length; ++i)
				memhc ^= gr_mem[i].GetHashCode ();

			return gr_name.GetHashCode () ^ gr_passwd.GetHashCode () ^ 
				gr_gid.GetHashCode () ^ memhc;
		}

		public override bool Equals (object obj)
		{
			if (obj == null || GetType() != obj.GetType())
				return false;
			Group g = (Group) obj;
			return Equals (g);
		}

		public bool Equals (Group value)
		{
			if (value == null)
				return false;
			if (value.gr_gid != gr_gid)
				return false;
			if (value.gr_gid == gr_gid && value.gr_name == gr_name &&
				value.gr_passwd == gr_passwd) {
				if (value.gr_mem == gr_mem)
					return true;
				if (value.gr_mem == null || gr_mem == null)
					return false;
				if (value.gr_mem.Length != gr_mem.Length)
					return false;
				for (int i = 0; i < gr_mem.Length; ++i)
					if (gr_mem[i] != value.gr_mem[i])
						return false;
				return true;
			}
			return false;
		}

		// Generate string in /etc/group format
		public override string ToString ()
		{
			StringBuilder sb = new StringBuilder ();
			sb.Append (gr_name).Append (":").Append (gr_passwd).Append (":");
			sb.Append (gr_gid).Append (":");
			GetMembers (sb, gr_mem);
			return sb.ToString ();
		}

		private static void GetMembers (StringBuilder sb, string[] members)
		{
			if (members.Length > 0)
				sb.Append (members[0]);
			for (int i = 1; i < members.Length; ++i) {
				sb.Append (",");
				sb.Append (members[i]);
			}
		}

		public static bool operator== (Group lhs, Group rhs)
		{
			return Object.Equals (lhs, rhs);
		}

		public static bool operator!= (Group lhs, Group rhs)
		{
			return !Object.Equals (lhs, rhs);
		}
	}

	public sealed class Passwd
		: IEquatable <Passwd>
	{
		public string           pw_name;
		public string           pw_passwd;
		[CLSCompliant (false)]
		public /* uid_t */ uint pw_uid;
		[CLSCompliant (false)]
		public /* gid_t */ uint pw_gid;
		public string           pw_gecos;
		public string           pw_dir;
		public string           pw_shell;

		public override int GetHashCode ()
		{
			return pw_name.GetHashCode () ^ pw_passwd.GetHashCode () ^ 
				pw_uid.GetHashCode () ^ pw_gid.GetHashCode () ^
				pw_gecos.GetHashCode () ^ pw_dir.GetHashCode () ^
				pw_dir.GetHashCode () ^ pw_shell.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || GetType() != obj.GetType())
				return false;
			Passwd p = (Passwd) obj;
			return Equals (p);
		}

		public bool Equals (Passwd value)
		{
			if (value == null)
				return false;
			return value.pw_uid == pw_uid && value.pw_gid == pw_gid && 
				value.pw_name == pw_name && value.pw_passwd == pw_passwd && 
				value.pw_gecos == pw_gecos && value.pw_dir == pw_dir && 
				value.pw_shell == pw_shell;
		}

		// Generate string in /etc/passwd format
		public override string ToString ()
		{
			return string.Format ("{0}:{1}:{2}:{3}:{4}:{5}:{6}",
				pw_name, pw_passwd, pw_uid, pw_gid, pw_gecos, pw_dir, pw_shell);
		}

		public static bool operator== (Passwd lhs, Passwd rhs)
		{
			return Object.Equals (lhs, rhs);
		}

		public static bool operator!= (Passwd lhs, Passwd rhs)
		{
			return !Object.Equals (lhs, rhs);
		}
	}

	public sealed class Utsname
		: IEquatable <Utsname>
	{
		public string sysname;
		public string nodename;
		public string release;
		public string version;
		public string machine;
		public string domainname;

		public override int GetHashCode ()
		{
			return sysname.GetHashCode () ^ nodename.GetHashCode () ^ 
				release.GetHashCode () ^ version.GetHashCode () ^
				machine.GetHashCode () ^ domainname.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (obj == null || GetType() != obj.GetType())
				return false;
			Utsname u = (Utsname) obj;
			return Equals (u);
		}

		public bool Equals (Utsname value)
		{
			return value.sysname == sysname && value.nodename == nodename && 
				value.release == release && value.version == version && 
				value.machine == machine && value.domainname == domainname;
		}

		// Generate string in /etc/passwd format
		public override string ToString ()
		{
			return string.Format ("{0} {1} {2} {3} {4}",
				sysname, nodename, release, version, machine);
		}

		public static bool operator== (Utsname lhs, Utsname rhs)
		{
			return Object.Equals (lhs, rhs);
		}

		public static bool operator!= (Utsname lhs, Utsname rhs)
		{
			return !Object.Equals (lhs, rhs);
		}
	}

	// This struct is used by the native code.
	// Its layout must be the same as the start of the Sockaddr class and the start of the _SockaddrDynamic struct
	[Map]
	[StructLayout (LayoutKind.Sequential)]
	internal struct _SockaddrHeader {
		internal SockaddrType type;
		internal UnixAddressFamily sa_family;
	}

	// Base class for all Sockaddr types.
	// This class is not abstract, instances of this class can be used to determine the sa_family value.
	// This class and all classes which are deriving from it and are passed to the native code have to be blittable.
	[CLSCompliant (false)]
	[StructLayout (LayoutKind.Sequential)]
	public class Sockaddr {
		// Note: the layout of the first members must match the layout of struct _SockaddrHeader
		// 'type' must be the first field of the class as it is used to find the address of the class itself
		internal SockaddrType type;
		internal UnixAddressFamily _sa_family;

		public UnixAddressFamily sa_family {
			get { return _sa_family; }
			set { _sa_family = value; }
		}

		public Sockaddr ()
		{
			this.type = SockaddrType.Sockaddr;
			this.sa_family = UnixAddressFamily.AF_UNSPEC;
		}

		internal Sockaddr (SockaddrType type, UnixAddressFamily sa_family)
		{
			this.type = type;
			this.sa_family = sa_family;
		}

		[DllImport (Syscall.MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Sockaddr_GetNativeSize")]
		static extern unsafe int GetNativeSize (_SockaddrHeader* address, out long size);

		internal unsafe long GetNativeSize ()
		{
			long size;
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (this).type)
			fixed (byte* data = Sockaddr.GetDynamicData (this)) {
				var dyn = new _SockaddrDynamic (this, data, useMaxLength: false);
				if (GetNativeSize (Sockaddr.GetNative (&dyn, addr), out size) != 0)
						throw new ArgumentException ("Failed to get size of native struct", "this");
			}
			return size;
		}


		// In order to create a wrapper for a syscall which accepts a "struct sockaddr" argument but does not modify it, use:

		// fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
		// fixed (byte* data = Sockaddr.GetDynamicData (address)) {
		//     var dyn = new _SockaddrDynamic (address, data, useMaxLength: false);
		//     return sys_syscall (..., Sockaddr.GetNative (&dyn, addr));
		// }

		// For syscalls which modify the argument, use:

		// fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
		// fixed (byte* data = Sockaddr.GetDynamicData (address)) {
		//     var dyn = new _SockaddrDynamic (address, data, useMaxLength: true);
		//     rettype r = sys_syscall (..., Sockaddr.GetNative (&dyn, addr));
		//     dyn.Update (address);
		//     return r;
		// }

		// This sequence will handle
		// - normal Sockaddrs like SockaddrIn and SockaddrIn6 which will be passed directly,
		// - sockaddrs like SockaddrUn and SockaddrStorage which need a wrapper and
		// - null (which will be passed as null)
		// without any heap memory allocations.


		// This is a fake Sockaddr which is passed to the fixed() statement if the address was null.
		// Sockaddr.GetNative() will return a null pointer for this Sockaddr.
		static Sockaddr nullSockaddr = new Sockaddr ();

		internal static Sockaddr GetAddress (Sockaddr address)
		{
			if (address == null)
				return nullSockaddr;
			else
				return address;
		}

		internal static unsafe _SockaddrHeader* GetNative (_SockaddrDynamic* dyn, SockaddrType* addr)
		{
			if (dyn->data != null) {
				return (_SockaddrHeader*) dyn;
			} else {
				fixed (SockaddrType* nullType = &nullSockaddr.type)
					if (addr == nullType)
						return null;
				return (_SockaddrHeader*) addr;
			}
		}

		// Return an array containing the dynamic data (for SockaddrStorage and SockaddrUn) or null
		internal static byte[] GetDynamicData (Sockaddr addr)
		{
			if (addr == null)
				return null;
			return addr.DynamicData ();
		}

		// This methods is overwritten in SockaddrStorage and SockaddrUn
		internal virtual byte[] DynamicData ()
		{
			return null;
		}

		// This methods should only be called for SockaddrStorage and SockaddrUn where they are overwritten
		internal virtual long GetDynamicLength ()
		{
			throw new NotImplementedException ();
		}

		internal virtual void SetDynamicLength (long value)
		{
			throw new NotImplementedException ();
		}

		public SockaddrStorage ToSockaddrStorage ()
		{
			var storage = new SockaddrStorage ((int) GetNativeSize ());
			storage.SetTo (this);
			return storage;
		}

		public static Sockaddr FromSockaddrStorage (SockaddrStorage storage)
		{
			var ret = new Sockaddr ();
			storage.CopyTo (ret);
			return ret;
		}
	}

	// This struct is required to manually marshal Sockaddr* classes which include an array (currently SockaddrStorage and SockaddrUn).
	// This is needed because the marshalling code will not work if the classes derived from Sockaddr aren't blittable.
	[Map]
	unsafe struct _SockaddrDynamic {
		// Note: the layout of the first members must match the layout of struct _SockaddrHeader
		public SockaddrType type;
		public UnixAddressFamily sa_family;
		public byte* data;
		public long len;

		public _SockaddrDynamic (Sockaddr address, byte* data, bool useMaxLength)
		{
			if (data == null) {
				// When data is null, no wrapper is needed.
				// Initialize everything to zero, Sockaddr.GetNative() will then
				// use the Sockaddr structure directly.
				this = new _SockaddrDynamic ();
				return;
			}

			var dynData = address.DynamicData ();

			type = address.type & ~SockaddrType.MustBeWrapped;
			sa_family = address.sa_family;
			this.data = data;
			if (useMaxLength) {
				len = dynData.Length;
			} else {
				len = address.GetDynamicLength ();
				if (len < 0 || len > dynData.Length)
					throw new ArgumentException ("len < 0 || len > dynData.Length", "address");
			}
		}

		public void Update (Sockaddr address)
		{
			// When data is null, no wrapper was needed.
			if (data == null)
				return;

			address.sa_family = sa_family;
			address.SetDynamicLength (len);
		}
	};

	// This is a class which can store arbitrary sockaddrs, even if they are not known the the Mono.Unix wrapper or the family does not have a corresponding value in the UnixAddressFamily enumeration.
	[CLSCompliant (false)]
	public sealed class SockaddrStorage : Sockaddr, IEquatable<SockaddrStorage> {
		// Note: The sa_family field is ignored when passing a SockaddrStorage to a syscall (but it will be set when a SockaddrStorage is returned from a syscall). Instead of the sa_family field, the value embedded in data is used.
		public byte[] data { get; set; }
		public long data_len { get; set; }

		internal override byte[] DynamicData ()
		{
			return data;
		}

		internal override long GetDynamicLength ()
		{
			return data_len;
		}

		internal override void SetDynamicLength (long value)
		{
			data_len = value;
		}

		[DllImport (Syscall.MPH, SetLastError=true,
				EntryPoint="Mono_Posix_SockaddrStorage_get_size")]
		static extern int get_size ();
		static readonly int default_size = get_size ();

		public SockaddrStorage ()
			: base (SockaddrType.SockaddrStorage | SockaddrType.MustBeWrapped, UnixAddressFamily.AF_UNSPEC)
		{
			data = new byte[default_size];
			data_len = 0;
		}

		public SockaddrStorage (int size)
			: base (SockaddrType.SockaddrStorage | SockaddrType.MustBeWrapped, UnixAddressFamily.AF_UNSPEC)
		{
			data = new byte[size];
			data_len = 0;
		}

		public unsafe void SetTo (Sockaddr address)
		{
			if (address == null)
				throw new ArgumentNullException ("address");

			var size = address.GetNativeSize ();
			if (size > data.Length)
				data = new byte[size];
			fixed (byte* ptr = data)
				if (!NativeConvert.TryCopy (address, (IntPtr) ptr))
					throw new ArgumentException ("Failed to convert to native struct", "address");
			data_len = size;
			sa_family = address.sa_family;
		}

		public unsafe void CopyTo (Sockaddr address)
		{
			if (address == null)
				throw new ArgumentNullException ("address");
			if (data_len < 0 || data_len > data.Length)
				throw new ArgumentException ("data_len < 0 || data_len > data.Length", "this");

			fixed (byte* ptr = data)
				if (!NativeConvert.TryCopy ((IntPtr) ptr, data_len, address))
					throw new ArgumentException ("Failed to convert from native struct", "this");
		}

		public override string ToString ()
		{
			var sb = new StringBuilder ();
			sb.AppendFormat ("{{sa_family={0}, data_len={1}, data=(", sa_family, data_len);
			for (int i = 0; i < data_len; i++) {
				if (i != 0)
					sb.Append (" ");
				sb.Append (data[i].ToString ("x2"));
			}
			sb.Append (")");
			return sb.ToString ();
		}

		public override int GetHashCode ()
		{
			unchecked {
				int hash = 0x1234;
				for (int i = 0; i < data_len; i++)
					hash += i ^ data[i];
				return hash;
			}
		}

		public override bool Equals (object obj)
		{
			if (!(obj is SockaddrStorage))
				return false;
			return Equals ((SockaddrStorage) obj);
		}

		public bool Equals (SockaddrStorage value)
		{
			if (value == null)
				return false;
			if (data_len != value.data_len)
				return false;
			for (int i = 0; i < data_len; i++)
				if (data[i] != value.data[i])
					return false;
			return true;
		}
	}

	[CLSCompliant (false)]
	public sealed class SockaddrUn : Sockaddr, IEquatable<SockaddrUn> {
		public UnixAddressFamily sun_family { // AF_UNIX
			get { return sa_family; }
			set { sa_family = value; }
		}
		public byte[] sun_path { get; set; }
		public long sun_path_len { get; set; } // Indicates how many bytes of sun_path are valid. Must not be larger than sun_path.Length.

		internal override byte[] DynamicData ()
		{
			return sun_path;
		}

		internal override long GetDynamicLength ()
		{
			return sun_path_len;
		}

		internal override void SetDynamicLength (long value)
		{
			sun_path_len = value;
		}

		[DllImport (Syscall.MPH, SetLastError=true,
				EntryPoint="Mono_Posix_SockaddrUn_get_sizeof_sun_path")]
		static extern int get_sizeof_sun_path ();
		static readonly int sizeof_sun_path = get_sizeof_sun_path ();

		public SockaddrUn ()
			: base (SockaddrType.SockaddrUn | SockaddrType.MustBeWrapped, UnixAddressFamily.AF_UNIX)
		{
			sun_path = new byte[sizeof_sun_path];
			sun_path_len = 0;
		}

		public SockaddrUn (int size)
			: base (SockaddrType.SockaddrUn | SockaddrType.MustBeWrapped, UnixAddressFamily.AF_UNIX)
		{
			sun_path = new byte[size];
			sun_path_len = 0;
		}

		public SockaddrUn (string path, bool linuxAbstractNamespace = false)
			: base (SockaddrType.SockaddrUn | SockaddrType.MustBeWrapped, UnixAddressFamily.AF_UNIX)
		{
			if (path == null)
				throw new ArgumentNullException ("path");
			var bytes = UnixEncoding.Instance.GetBytes (path);
			if (linuxAbstractNamespace) {
				sun_path = new byte[1 + bytes.Length];
				Array.Copy (bytes, 0, sun_path, 1, bytes.Length);
			} else {
				sun_path = bytes;
			}
			sun_path_len = sun_path.Length;
		}

		public bool IsLinuxAbstractNamespace {
			get {
				return sun_path_len > 0 && sun_path[0] == 0;
			}
		}

		public string Path {
			get {
				var offset = IsLinuxAbstractNamespace ? 1 : 0;
				// Remove data after null terminator
				int length;
				for (length = 0; offset + length < sun_path_len; length++)
					if (sun_path[offset + length] == 0)
						break;
				return UnixEncoding.Instance.GetString (sun_path, offset, length);
			}
		}

		public override string ToString ()
		{
			return string.Format ("{{sa_family={0}, sun_path=\"{1}{2}\"}}", sa_family, IsLinuxAbstractNamespace ? "\\0" : "", Path);
		}

		public static new SockaddrUn FromSockaddrStorage (SockaddrStorage storage)
		{
			// This will make the SockaddrUn larger than it needs to be (because
			// storage.data_len includes the sun_family field), but it will be
			// large enough.
			var ret = new SockaddrUn ((int) storage.data_len);
			storage.CopyTo (ret);
			return ret;
		}

		public override int GetHashCode ()
		{
			return sun_family.GetHashCode () ^ IsLinuxAbstractNamespace.GetHashCode () ^ Path.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (!(obj is SockaddrUn))
				return false;
			return Equals ((SockaddrUn) obj);
		}

		public bool Equals (SockaddrUn value)
		{
			if (value == null)
				return false;
			return sun_family == value.sun_family
				&& IsLinuxAbstractNamespace == value.IsLinuxAbstractNamespace
				&& Path == value.Path;
		}
	}

	[Map ("struct sockaddr_in")]
	[CLSCompliant (false)]
	[StructLayout (LayoutKind.Sequential)]
	public sealed class SockaddrIn : Sockaddr, IEquatable<SockaddrIn> {
		public UnixAddressFamily sin_family { // AF_INET
			get { return sa_family; }
			set { sa_family = value; }
		}
		public ushort sin_port;   // Port number.
		public InAddr sin_addr;   // IP address.

		public SockaddrIn ()
			: base (SockaddrType.SockaddrIn, UnixAddressFamily.AF_INET)
		{
		}

		public override string ToString ()
		{
			return string.Format ("{{sin_family={0}, sin_port=htons({1}), sin_addr={2}}}", sa_family, Syscall.ntohs(sin_port), sin_addr);
		}

		public static new SockaddrIn FromSockaddrStorage (SockaddrStorage storage)
		{
			var ret = new SockaddrIn ();
			storage.CopyTo (ret);
			return ret;
		}

		public override int GetHashCode ()
		{
			return sin_family.GetHashCode () ^ sin_port.GetHashCode () ^ sin_addr.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (!(obj is SockaddrIn))
				return false;
			return Equals ((SockaddrIn) obj);
		}

		public bool Equals (SockaddrIn value)
		{
			if (value == null)
				return false;
			return sin_family == value.sin_family
				&& sin_port == value.sin_port
				&& sin_addr.Equals (value.sin_addr);
		}
	}

	[Map ("struct sockaddr_in6")]
	[CLSCompliant (false)]
	[StructLayout (LayoutKind.Sequential)]
	public sealed class SockaddrIn6 : Sockaddr, IEquatable<SockaddrIn6> {
		public UnixAddressFamily sin6_family { // AF_INET6
			get { return sa_family; }
			set { sa_family = value; }
		}
		public ushort  sin6_port;     // Port number.
		public uint    sin6_flowinfo; // IPv6 traffic class and flow information.
		public In6Addr sin6_addr;     // IPv6 address.
		public uint    sin6_scope_id; // Set of interfaces for a scope.

		public SockaddrIn6 ()
			: base (SockaddrType.SockaddrIn6, UnixAddressFamily.AF_INET6)
		{
		}

		public override string ToString ()
		{
			return string.Format ("{{sin6_family={0}, sin6_port=htons({1}), sin6_flowinfo={2}, sin6_addr={3}, sin6_scope_id={4}}}", sa_family, Syscall.ntohs (sin6_port), sin6_flowinfo, sin6_addr, sin6_scope_id);
		}

		public static new SockaddrIn6 FromSockaddrStorage (SockaddrStorage storage)
		{
			var ret = new SockaddrIn6 ();
			storage.CopyTo (ret);
			return ret;
		}

		public override int GetHashCode ()
		{
			return sin6_family.GetHashCode () ^ sin6_port.GetHashCode () ^ sin6_flowinfo.GetHashCode () ^ sin6_addr.GetHashCode () ^ sin6_scope_id.GetHashCode ();
		}

		public override bool Equals (object obj)
		{
			if (!(obj is SockaddrIn6))
				return false;
			return Equals ((SockaddrIn6) obj);
		}

		public bool Equals (SockaddrIn6 value)
		{
			if (value == null)
				return false;
			return sin6_family == value.sin6_family
				&& sin6_port == value.sin6_port
				&& sin6_flowinfo == value.sin6_flowinfo
				&& sin6_addr.Equals (value.sin6_addr)
				&& sin6_scope_id == value.sin6_scope_id;
		}
	}

	[CLSCompliant (false)]
	public sealed class Msghdr
	{
		public Sockaddr msg_name;
		// msg_name_len is part of the Sockaddr structure
		public Iovec[] msg_iov;
		public int msg_iovlen;
		public byte[] msg_control;
		public long msg_controllen;
		public MessageFlags msg_flags;
	}

	//
	// Convention: Functions *not* part of the standard C library AND part of
	// a POSIX and/or Unix standard (X/Open, SUS, XPG, etc.) go here.
	//
	// For example, the man page should be similar to:
	//
	//    CONFORMING TO (or CONFORMS TO)
	//           XPG2, SUSv2, POSIX, etc.
	//
	// BSD- and GNU-specific exports can also be placed here.
	//
	// Non-POSIX/XPG/etc. functions can also be placed here if:
	//  (a) They'd be likely to be covered in a Steven's-like book
	//  (b) The functions would be present in libc.so (or equivalent).
	//
	// If a function has its own library, that's a STRONG indicator that the
	// function should get a different binding, probably in its own assembly, 
	// so that package management can work sanely.  (That is, we'd like to avoid
	// scenarios where FooLib.dll is installed, but it requires libFooLib.so to
	// run, and libFooLib.so doesn't exist.  That would be confusing.)
	//
	// The only methods in here should be:
	//  (1) low-level functions
	//  (2) "Trivial" function overloads.  For example, if the parameters to a
	//      function are related (e.g. getgroups(2))
	//  (3) The return type SHOULD NOT be changed.  If you want to provide a
	//      convenience function with a nicer return type, place it into one of
	//      the Mono.Unix.Unix* wrapper classes, and give it a .NET-styled name.
	//      - EXCEPTION: No public functions should have a `void' return type.
	//        `void' return types should be replaced with `int'.
	//        Rationality: `void'-return functions typically require a
	//        complicated call sequence, such as clear errno, then call, then
	//        check errno to see if any errors occurred.  This sequence can't 
	//        be done safely in managed code, as errno may change as part of 
	//        the P/Invoke mechanism.
	//        Instead, add a MonoPosixHelper export which does:
	//          errno = 0;
	//          INVOKE SYSCALL;
	//          return errno == 0 ? 0 : -1;
	//        This lets managed code check the return value in the usual manner.
	//  (4) Exceptions SHOULD NOT be thrown.  EXCEPTIONS: 
	//      - If you're wrapping *broken* methods which make assumptions about 
	//        input data, such as that an argument refers to N bytes of data.  
	//        This is currently limited to cuserid(3) and encrypt(3).
	//      - If you call functions which themselves generate exceptions.  
	//        This is the case for using NativeConvert, which will throw an
	//        exception if an invalid/unsupported value is used.
	//
	// Naming Conventions:
	//  - Syscall method names should have the same name as the function being
	//    wrapped (e.g. Syscall.read ==> read(2)).  This allows people to
	//    consult the appropriate man page if necessary.
	//  - Methods need not have the same arguments IF this simplifies or
	//    permits correct usage.  The current example is syslog, in which
	//    syslog(3)'s single `priority' argument is split into SyslogFacility
	//    and SyslogLevel arguments.
	//  - Type names (structures, classes, enumerations) are always PascalCased.
	//  - Enumerations are named as <MethodName><ArgumentName>, and are located
	//    in the Mono.Unix.Native namespace.  For readability, if ArgumentName 
	//    is "cmd", use Command instead.  For example, fcntl(2) takes a
	//    FcntlCommand argument.  This naming convention is to provide an
	//    assocation between an enumeration and where it should be used, and
	//    allows a single method to accept multiple different enumerations 
	//    (see mmap(2), which takes MmapProts and MmapFlags).
	//    - EXCEPTION: if an enumeration is shared between multiple different
	//      methods, AND/OR the "obvious" enumeration name conflicts with an
	//      existing .NET type, a more appropriate name should be used.
	//      Example: FilePermissions
	//    - EXCEPTION: [Flags] enumerations should get plural names to follow
	//      .NET name guidelines.  Usually this doesn't result in a change
	//      (OpenFlags is the `flags' parameter for open(2)), but it can
	//      (mmap(2) prot ==> MmapProts, access(2) mode ==> AccessModes).
	//  - Enumerations should have the [Map] and (optional) [Flags] attributes.
	//    [Map] is required for make-map to find the type and generate the
	//    appropriate NativeConvert conversion functions.
	//  - Enumeration contents should match the original Unix names.  This helps
	//    with documentation (the existing man pages are still useful), and is
	//    required for use with the make-map generation program.
	//  - Structure names should be the PascalCased version of the actual
	//    structure name (struct flock ==> Flock).  Structure members should
	//    have the same names, or a (reasonably) portable subset (Dirent being
	//    the poster child for questionable members).
	//    - Whether the managed type should be a reference type (class) or a 
	//      value type (struct) should be determined on a case-by-case basis: 
	//      if you ever need to be able to use NULL for it (such as with Dirent, 
	//      Group, Passwd, as these are method return types and `null' is used 
	//      to signify the end), it should be a reference type; otherwise, use 
	//      your discretion, and keep any expected usage patterns in mind.
	//  - Syscall should be a Single Point Of Truth (SPOT).  There should be
	//    only ONE way to do anything.  By convention, the Linux function names
	//    are used, but that need not always be the case (use your discretion).
	//    It SHOULD NOT be required that developers know what platform they're
	//    on, and choose among a set of similar functions.  In short, anything
	//    that requires a platform check is BAD -- Mono.Unix is a wrapper, and
	//    we can afford to clean things up whenever possible.
	//    - Examples: 
	//    	- Syscall.statfs: Solaris/Mac OS X provide statfs(2), Linux provides
	//        statvfs(2).  MonoPosixHelper will "thunk" between the two,
	//        exporting a statvfs that works across platforms.
	//    	- Syscall.getfsent: Glibc export which Solaris lacks, while Solaris
	//    	  instead provides getvfsent(3).  MonoPosixHelper provides wrappers
	//    	  to convert getvfsent(3) into Fstab data.
	//    - Exception: If it isn't possible to cleanly wrap platforms, then the
	//      method shouldn't be exported.  The user will be expected to do their
	//      own platform check and their own DllImports.
	//      Examples: mount(2), umount(2), etc.
	//    - Note: if a platform doesn't support a function AT ALL, the
	//      MonoPosixHelper wrapper won't be compiled, resulting in a
	//      EntryPointNotFoundException.  This is also consistent with a missing 
	//      P/Invoke into libc.so.
	//
	[CLSCompliant (false)]
	public sealed class Syscall : Stdlib
	{
		new internal const string LIBC  = "libc";

		private Syscall () {}

		//
		// <aio.h>
		//

		// TODO: aio_cancel(3), aio_error(3), aio_fsync(3), aio_read(3), 
		// aio_return(3), aio_suspend(3), aio_write(3)
		//
		// Then update UnixStream.BeginRead to use the aio* functions.


		#region <attr/xattr.h> Declarations
		//
		// <attr/xattr.h> -- COMPLETE
		//

		// setxattr(2)
		//    int setxattr (const char *path, const char *name,
		//        const void *value, size_t size, int flags);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_setxattr")]
		public static extern int setxattr (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name, byte[] value, ulong size, XattrFlags flags);

		public static int setxattr (string path, string name, byte [] value, ulong size)
		{
			return setxattr (path, name, value, size, XattrFlags.XATTR_AUTO);
		}

		public static int setxattr (string path, string name, byte [] value, XattrFlags flags)
		{
			return setxattr (path, name, value, (ulong) value.Length, flags);
		}

		public static int setxattr (string path, string name, byte [] value)
		{
			return setxattr (path, name, value, (ulong) value.Length);
		}

		// lsetxattr(2)
		// 	  int lsetxattr (const char *path, const char *name,
		//                   const void *value, size_t size, int flags);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_lsetxattr")]
		public static extern int lsetxattr (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name, byte[] value, ulong size, XattrFlags flags);

		public static int lsetxattr (string path, string name, byte [] value, ulong size)
		{
			return lsetxattr (path, name, value, size, XattrFlags.XATTR_AUTO);
		}

		public static int lsetxattr (string path, string name, byte [] value, XattrFlags flags)
		{
			return lsetxattr (path, name, value, (ulong) value.Length, flags);
		}

		public static int lsetxattr (string path, string name, byte [] value)
		{
			return lsetxattr (path, name, value, (ulong) value.Length);
		}

		// fsetxattr(2)
		// 	  int fsetxattr (int fd, const char *name,
		//                   const void *value, size_t size, int flags);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_fsetxattr")]
		public static extern int fsetxattr (int fd, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name, byte[] value, ulong size, XattrFlags flags);

		public static int fsetxattr (int fd, string name, byte [] value, ulong size)
		{
			return fsetxattr (fd, name, value, size, XattrFlags.XATTR_AUTO);
		}

		public static int fsetxattr (int fd, string name, byte [] value, XattrFlags flags)
		{
			return fsetxattr (fd, name, value, (ulong) value.Length, flags);
		}

		public static int fsetxattr (int fd, string name, byte [] value)
		{
			return fsetxattr (fd, name, value, (ulong) value.Length);
		}

		// getxattr(2)
		// 	  ssize_t getxattr (const char *path, const char *name,
		//                      void *value, size_t size);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getxattr")]
		public static extern long getxattr (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name, byte[] value, ulong size);

		public static long getxattr (string path, string name, byte [] value)
		{
			return getxattr (path, name, value, (ulong) value.Length);
		}

		public static long getxattr (string path, string name, out byte [] value)
		{
			value = null;
			long size = getxattr (path, name, value, 0);
			if (size <= 0)
				return size;

			value = new byte [size];
			return getxattr (path, name, value, (ulong) size);
		}

		// lgetxattr(2)
		// 	  ssize_t lgetxattr (const char *path, const char *name,
		//                       void *value, size_t size);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_lgetxattr")]
		public static extern long lgetxattr (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name, byte[] value, ulong size);

		public static long lgetxattr (string path, string name, byte [] value)
		{
			return lgetxattr (path, name, value, (ulong) value.Length);
		}

		public static long lgetxattr (string path, string name, out byte [] value)
		{
			value = null;
			long size = lgetxattr (path, name, value, 0);
			if (size <= 0)
				return size;

			value = new byte [size];
			return lgetxattr (path, name, value, (ulong) size);
		}

		// fgetxattr(2)
		// 	  ssize_t fgetxattr (int fd, const char *name, void *value, size_t size);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_fgetxattr")]
		public static extern long fgetxattr (int fd, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name, byte[] value, ulong size);

		public static long fgetxattr (int fd, string name, byte [] value)
		{
			return fgetxattr (fd, name, value, (ulong) value.Length);
		}

		public static long fgetxattr (int fd, string name, out byte [] value)
		{
			value = null;
			long size = fgetxattr (fd, name, value, 0);
			if (size <= 0)
				return size;

			value = new byte [size];
			return fgetxattr (fd, name, value, (ulong) size);
		}

		// listxattr(2)
		// 	  ssize_t listxattr (const char *path, char *list, size_t size);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_listxattr")]
		public static extern long listxattr (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, byte[] list, ulong size);

		// Slight modification: returns 0 on success, negative on error
		public static long listxattr (string path, Encoding encoding, out string [] values)
		{
			values = null;
			long size = listxattr (path, null, 0);
			if (size == 0)
				values = new string [0];
			if (size <= 0)
				return (int) size;

			byte[] list = new byte [size];
			long ret = listxattr (path, list, (ulong) size);
			if (ret < 0)
				return (int) ret;

			GetValues (list, encoding, out values);
			return 0;
		}

		public static long listxattr (string path, out string[] values)
		{
			return listxattr (path, UnixEncoding.Instance, out values);
		}

		private static void GetValues (byte[] list, Encoding encoding, out string[] values)
		{
			int num_values = 0;
			for (int i = 0; i < list.Length; ++i)
				if (list [i] == 0)
					++num_values;

			values = new string [num_values];
			num_values = 0;
			int str_start = 0;
			for (int i = 0; i < list.Length; ++i) {
				if (list [i] == 0) {
					values [num_values++] = encoding.GetString (list, str_start, i - str_start);
					str_start = i+1;
				}
			}
		}

		// llistxattr(2)
		// 	  ssize_t llistxattr (const char *path, char *list, size_t size);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_llistxattr")]
		public static extern long llistxattr (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, byte[] list, ulong size);

		// Slight modification: returns 0 on success, negative on error
		public static long llistxattr (string path, Encoding encoding, out string [] values)
		{
			values = null;
			long size = llistxattr (path, null, 0);
			if (size == 0)
				values = new string [0];
			if (size <= 0)
				return (int) size;

			byte[] list = new byte [size];
			long ret = llistxattr (path, list, (ulong) size);
			if (ret < 0)
				return (int) ret;

			GetValues (list, encoding, out values);
			return 0;
		}

		public static long llistxattr (string path, out string[] values)
		{
			return llistxattr (path, UnixEncoding.Instance, out values);
		}

		// flistxattr(2)
		// 	  ssize_t flistxattr (int fd, char *list, size_t size);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_flistxattr")]
		public static extern long flistxattr (int fd, byte[] list, ulong size);

		// Slight modification: returns 0 on success, negative on error
		public static long flistxattr (int fd, Encoding encoding, out string [] values)
		{
			values = null;
			long size = flistxattr (fd, null, 0);
			if (size == 0)
				values = new string [0];
			if (size <= 0)
				return (int) size;

			byte[] list = new byte [size];
			long ret = flistxattr (fd, list, (ulong) size);
			if (ret < 0)
				return (int) ret;

			GetValues (list, encoding, out values);
			return 0;
		}

		public static long flistxattr (int fd, out string[] values)
		{
			return flistxattr (fd, UnixEncoding.Instance, out values);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_removexattr")]
		public static extern int removexattr (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name);

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_lremovexattr")]
		public static extern int lremovexattr (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name);

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_fremovexattr")]
		public static extern int fremovexattr (int fd, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name);
		#endregion

		#region <dirent.h> Declarations
		//
		// <dirent.h>
		//
		// TODO: scandir(3), alphasort(3), versionsort(3), getdirentries(3)

		[DllImport (LIBC, SetLastError=true)]
		public static extern IntPtr opendir (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int closedir (IntPtr dir);

		// seekdir(3):
		//    void seekdir (DIR *dir, off_t offset);
		//    Slight modification.  Returns -1 on error, 0 on success.
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_seekdir")]
		public static extern int seekdir (IntPtr dir, long offset);

		// telldir(3)
		//    off_t telldir(DIR *dir);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_telldir")]
		public static extern long telldir (IntPtr dir);

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_rewinddir")]
		public static extern int rewinddir (IntPtr dir);

		private struct _Dirent {
			[ino_t] public ulong      d_ino;
			[off_t] public long       d_off;
			public ushort             d_reclen;
			public byte               d_type;
			public IntPtr             d_name;
		}

		private static void CopyDirent (Dirent to, ref _Dirent from)
		{
			try {
				to.d_ino    = from.d_ino;
				to.d_off    = from.d_off;
				to.d_reclen = from.d_reclen;
				to.d_type   = from.d_type;
				to.d_name   = UnixMarshal.PtrToString (from.d_name);
			}
			finally {
				Stdlib.free (from.d_name);
				from.d_name = IntPtr.Zero;
			}
		}

		internal static object readdir_lock = new object ();

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_readdir")]
		private static extern int sys_readdir (IntPtr dir, out _Dirent dentry);

		public static Dirent readdir (IntPtr dir)
		{
			_Dirent dentry;
			int r;
			lock (readdir_lock) {
				r = sys_readdir (dir, out dentry);
			}
			if (r != 0)
				return null;
			Dirent d = new Dirent ();
			CopyDirent (d, ref dentry);
			return d;
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_readdir_r")]
		private static extern int sys_readdir_r (IntPtr dirp, out _Dirent entry, out IntPtr result);

		public static int readdir_r (IntPtr dirp, Dirent entry, out IntPtr result)
		{
			entry.d_ino    = 0;
			entry.d_off    = 0;
			entry.d_reclen = 0;
			entry.d_type   = 0;
			entry.d_name   = null;

			_Dirent _d;
			int r = sys_readdir_r (dirp, out _d, out result);

			if (r == 0 && result != IntPtr.Zero) {
				CopyDirent (entry, ref _d);
			}

			return r;
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int dirfd (IntPtr dir);

		[DllImport (LIBC, SetLastError=true)]
		public static extern IntPtr fdopendir (int fd);
		#endregion

		#region <fcntl.h> Declarations
		//
		// <fcntl.h> -- COMPLETE
		//

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_fcntl")]
		public static extern int fcntl (int fd, FcntlCommand cmd);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_fcntl_arg")]
		public static extern int fcntl (int fd, FcntlCommand cmd, long arg);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_fcntl_arg_int")]
		public static extern int fcntl (int fd, FcntlCommand cmd, int arg);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_fcntl_arg_ptr")]
		public static extern int fcntl (int fd, FcntlCommand cmd, IntPtr ptr);

		public static int fcntl (int fd, FcntlCommand cmd, DirectoryNotifyFlags arg)
		{
			if (cmd != FcntlCommand.F_NOTIFY) {
				SetLastError (Errno.EINVAL);
				return -1;
			}
			long _arg = NativeConvert.FromDirectoryNotifyFlags (arg);
			return fcntl (fd, FcntlCommand.F_NOTIFY, _arg);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_fcntl_lock")]
		public static extern int fcntl (int fd, FcntlCommand cmd, ref Flock @lock);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_open")]
		public static extern int open (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, OpenFlags flags);

		// open(2)
		//    int open(const char *pathname, int flags, mode_t mode);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_open_mode")]
		public static extern int open (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, OpenFlags flags, FilePermissions mode);

		// creat(2)
		//    int creat(const char *pathname, mode_t mode);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_creat")]
		public static extern int creat (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, FilePermissions mode);

		// posix_fadvise(2)
		//    int posix_fadvise(int fd, off_t offset, off_t len, int advice);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_posix_fadvise")]
		public static extern int posix_fadvise (int fd, long offset, 
			long len, PosixFadviseAdvice advice);

		// posix_fallocate(P)
		//    int posix_fallocate(int fd, off_t offset, size_t len);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_posix_fallocate")]
		public static extern int posix_fallocate (int fd, long offset, ulong len);

		[DllImport (LIBC, SetLastError=true, 
				EntryPoint="openat")]
		private static extern int sys_openat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, int flags);

		// openat(2)
		//    int openat(int dirfd, const char *pathname, int flags, mode_t mode);
		[DllImport (LIBC, SetLastError=true, 
				EntryPoint="openat")]
		private static extern int sys_openat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, int flags, uint mode);

		public static int openat (int dirfd, string pathname, OpenFlags flags)
		{
			int _flags = NativeConvert.FromOpenFlags (flags);
			return sys_openat (dirfd, pathname, _flags);
		}

		public static int openat (int dirfd, string pathname, OpenFlags flags, FilePermissions mode)
		{
			int _flags = NativeConvert.FromOpenFlags (flags);
			uint _mode = NativeConvert.FromFilePermissions (mode);
			return sys_openat (dirfd, pathname, _flags, _mode);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_get_at_fdcwd")]
		private static extern int get_at_fdcwd ();

		public static readonly int AT_FDCWD = get_at_fdcwd ();

		#endregion

		#region <fstab.h> Declarations
		//
		// <fstab.h>  -- COMPLETE
		//
		[Map]
		private struct _Fstab {
			public IntPtr fs_spec;
			public IntPtr fs_file;
			public IntPtr fs_vfstype;
			public IntPtr fs_mntops;
			public IntPtr fs_type;
			public int    fs_freq;
			public int    fs_passno;
			public IntPtr _fs_buf_;
		}

		private static void CopyFstab (Fstab to, ref _Fstab from)
		{
			try {
				to.fs_spec     = UnixMarshal.PtrToString (from.fs_spec);
				to.fs_file     = UnixMarshal.PtrToString (from.fs_file);
				to.fs_vfstype  = UnixMarshal.PtrToString (from.fs_vfstype);
				to.fs_mntops   = UnixMarshal.PtrToString (from.fs_mntops);
				to.fs_type     = UnixMarshal.PtrToString (from.fs_type);
				to.fs_freq     = from.fs_freq;
				to.fs_passno   = from.fs_passno;
			}
			finally {
				Stdlib.free (from._fs_buf_);
				from._fs_buf_ = IntPtr.Zero;
			}
		}

		internal static object fstab_lock = new object ();

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_endfsent")]
		private static extern int sys_endfsent ();

		public static int endfsent ()
		{
			lock (fstab_lock) {
				return sys_endfsent ();
			}
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getfsent")]
		private static extern int sys_getfsent (out _Fstab fs);

		public static Fstab getfsent ()
		{
			_Fstab fsbuf;
			int r;
			lock (fstab_lock) {
				r = sys_getfsent (out fsbuf);
			}
			if (r != 0)
				return null;
			Fstab fs = new Fstab ();
			CopyFstab (fs, ref fsbuf);
			return fs;
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getfsfile")]
		private static extern int sys_getfsfile (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string mount_point, out _Fstab fs);

		public static Fstab getfsfile (string mount_point)
		{
			_Fstab fsbuf;
			int r;
			lock (fstab_lock) {
				r = sys_getfsfile (mount_point, out fsbuf);
			}
			if (r != 0)
				return null;
			Fstab fs = new Fstab ();
			CopyFstab (fs, ref fsbuf);
			return fs;
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getfsspec")]
		private static extern int sys_getfsspec (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string special_file, out _Fstab fs);

		public static Fstab getfsspec (string special_file)
		{
			_Fstab fsbuf;
			int r;
			lock (fstab_lock) {
				r = sys_getfsspec (special_file, out fsbuf);
			}
			if (r != 0)
				return null;
			Fstab fs = new Fstab ();
			CopyFstab (fs, ref fsbuf);
			return fs;
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_setfsent")]
		private static extern int sys_setfsent ();

		public static int setfsent ()
		{
			lock (fstab_lock) {
				return sys_setfsent ();
			}
		}

		#endregion

		#region <grp.h> Declarations
		//
		// <grp.h>
		//
		// TODO: putgrent(3), fgetgrent_r(), initgroups(3)

		// getgrouplist(2)
		[DllImport (LIBC, SetLastError=true, EntryPoint="getgrouplist")]
		private static extern int sys_getgrouplist (string user, uint grp, uint [] groups,ref int ngroups);

		public static Group [] getgrouplist (string username)
		{
			if (username == null)
				throw new ArgumentNullException ("username");
			if (username.Trim () == "")
				throw new ArgumentException ("Username cannot be empty", "username");
			// Syscall to getpwnam to retrieve user uid
			Passwd pw = Syscall.getpwnam (username);
			if (pw == null)
				throw new ArgumentException (string.Format ("User {0} does not exist", username), "username");
			return getgrouplist (pw);
		}

		public static Group [] getgrouplist (Passwd user)
		{
			if (user == null)
				throw new ArgumentNullException ("user");
			// initializing ngroups by 16 to get the group count
			int ngroups = 8;
			int res = -1;
			// allocating buffer to store group uid's
			uint [] groups=null;
			do {
				Array.Resize (ref groups, ngroups*=2);
				res = sys_getgrouplist (user.pw_name, user.pw_gid, groups, ref ngroups);
			}
			while (res == -1);
			List<Group> result = new List<Group> ();
			Group gr = null;
			for (int i = 0; i < res; i++) {
				gr = Syscall.getgrgid (groups [i]);
				if (gr != null)
					result.Add (gr);
			}
			return result.ToArray ();
		}

		// setgroups(2)
		//    int setgroups (size_t size, const gid_t *list);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_setgroups")]
		public static extern int setgroups (ulong size, uint[] list);

		public static int setgroups (uint [] list)
		{
			return setgroups ((ulong) list.Length, list);
		}

		[Map]
		private struct _Group
		{
			public IntPtr           gr_name;
			public IntPtr           gr_passwd;
			[gid_t] public uint     gr_gid;
			public int              _gr_nmem_;
			public IntPtr           gr_mem;
			public IntPtr           _gr_buf_;
		}

		private static void CopyGroup (Group to, ref _Group from)
		{
			try {
				to.gr_gid    = from.gr_gid;
				to.gr_name   = UnixMarshal.PtrToString (from.gr_name);
				to.gr_passwd = UnixMarshal.PtrToString (from.gr_passwd);
				to.gr_mem    = UnixMarshal.PtrToStringArray (from._gr_nmem_, from.gr_mem);
			}
			finally {
				Stdlib.free (from.gr_mem);
				Stdlib.free (from._gr_buf_);
				from.gr_mem   = IntPtr.Zero;
				from._gr_buf_ = IntPtr.Zero;
			}
		}

		internal static object grp_lock = new object ();

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getgrnam")]
		private static extern int sys_getgrnam (string name, out _Group group);

		public static Group getgrnam (string name)
		{
			_Group group;
			int r;
			lock (grp_lock) {
				r = sys_getgrnam (name, out group);
			}
			if (r != 0)
				return null;
			Group gr = new Group ();
			CopyGroup (gr, ref group);
			return gr;
		}

		// getgrgid(3)
		//    struct group *getgrgid(gid_t gid);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getgrgid")]
		private static extern int sys_getgrgid (uint uid, out _Group group);

		public static Group getgrgid (uint uid)
		{
			_Group group;
			int r;
			lock (grp_lock) {
				r = sys_getgrgid (uid, out group);
			}
			if (r != 0)
				return null;
			Group gr = new Group ();
			CopyGroup (gr, ref group);
			return gr;
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getgrnam_r")]
		private static extern int sys_getgrnam_r (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name, out _Group grbuf, out IntPtr grbufp);

		public static int getgrnam_r (string name, Group grbuf, out Group grbufp)
		{
			grbufp = null;
			_Group group;
			IntPtr _grbufp;
			int r = sys_getgrnam_r (name, out group, out _grbufp);
			if (r == 0 && _grbufp != IntPtr.Zero) {
				CopyGroup (grbuf, ref group);
				grbufp = grbuf;
			}
			return r;
		}

		// getgrgid_r(3)
		//    int getgrgid_r(gid_t gid, struct group *gbuf, char *buf,
		//        size_t buflen, struct group **gbufp);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getgrgid_r")]
		private static extern int sys_getgrgid_r (uint uid, out _Group grbuf, out IntPtr grbufp);

		public static int getgrgid_r (uint uid, Group grbuf, out Group grbufp)
		{
			grbufp = null;
			_Group group;
			IntPtr _grbufp;
			int r = sys_getgrgid_r (uid, out group, out _grbufp);
			if (r == 0 && _grbufp != IntPtr.Zero) {
				CopyGroup (grbuf, ref group);
				grbufp = grbuf;
			}
			return r;
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getgrent")]
		private static extern int sys_getgrent (out _Group grbuf);

		public static Group getgrent ()
		{
			_Group group;
			int r;
			lock (grp_lock) {
				r = sys_getgrent (out group);
			}
			if (r != 0)
				return null;
			Group gr = new Group();
			CopyGroup (gr, ref group);
			return gr;
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_setgrent")]
		private static extern int sys_setgrent ();

		public static int setgrent ()
		{
			lock (grp_lock) {
				return sys_setgrent ();
			}
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_endgrent")]
		private static extern int sys_endgrent ();

		public static int endgrent ()
		{
			lock (grp_lock) {
				return sys_endgrent ();
			}
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_fgetgrent")]
		private static extern int sys_fgetgrent (IntPtr stream, out _Group grbuf);

		public static Group fgetgrent (IntPtr stream)
		{
			_Group group;
			int r;
			lock (grp_lock) {
				r = sys_fgetgrent (stream, out group);
			}
			if (r != 0)
				return null;
			Group gr = new Group ();
			CopyGroup (gr, ref group);
			return gr;
		}
		#endregion

		#region <pwd.h> Declarations
		//
		// <pwd.h>
		//
		// TODO: putpwent(3), fgetpwent_r()
		//
		// SKIPPING: getpw(3): it's dangerous.  Use getpwuid(3) instead.

		[Map]
		private struct _Passwd
		{
			public IntPtr           pw_name;
			public IntPtr           pw_passwd;
			[uid_t] public uint     pw_uid;
			[gid_t] public uint     pw_gid;
			public IntPtr           pw_gecos;
			public IntPtr           pw_dir;
			public IntPtr           pw_shell;
			public IntPtr           _pw_buf_;
		}

		private static void CopyPasswd (Passwd to, ref _Passwd from)
		{
			try {
				to.pw_name   = UnixMarshal.PtrToString (from.pw_name);
				to.pw_passwd = UnixMarshal.PtrToString (from.pw_passwd);
				to.pw_uid    = from.pw_uid;
				to.pw_gid    = from.pw_gid;
				to.pw_gecos  = UnixMarshal.PtrToString (from.pw_gecos);
				to.pw_dir    = UnixMarshal.PtrToString (from.pw_dir);
				to.pw_shell  = UnixMarshal.PtrToString (from.pw_shell);
			}
			finally {
				Stdlib.free (from._pw_buf_);
				from._pw_buf_ = IntPtr.Zero;
			}
		}

		internal static object pwd_lock = new object ();

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getpwnam")]
		private static extern int sys_getpwnam (string name, out _Passwd passwd);

		public static Passwd getpwnam (string name)
		{
			_Passwd passwd;
			int r;
			lock (pwd_lock) {
				r = sys_getpwnam (name, out passwd);
			}
			if (r != 0)
				return null;
			Passwd pw = new Passwd ();
			CopyPasswd (pw, ref passwd);
			return pw;
		}

		// getpwuid(3)
		//    struct passwd *getpwnuid(uid_t uid);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getpwuid")]
		private static extern int sys_getpwuid (uint uid, out _Passwd passwd);

		public static Passwd getpwuid (uint uid)
		{
			_Passwd passwd;
			int r;
			lock (pwd_lock) {
				r = sys_getpwuid (uid, out passwd);
			}
			if (r != 0)
				return null;
			Passwd pw = new Passwd ();
			CopyPasswd (pw, ref passwd);
			return pw;
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getpwnam_r")]
		private static extern int sys_getpwnam_r (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string name, out _Passwd pwbuf, out IntPtr pwbufp);

		public static int getpwnam_r (string name, Passwd pwbuf, out Passwd pwbufp)
		{
			pwbufp = null;
			_Passwd passwd;
			IntPtr _pwbufp;
			int r = sys_getpwnam_r (name, out passwd, out _pwbufp);
			if (r == 0 && _pwbufp != IntPtr.Zero) {
				CopyPasswd (pwbuf, ref passwd);
				pwbufp = pwbuf;
			}
			return r;
		}

		// getpwuid_r(3)
		//    int getpwuid_r(uid_t uid, struct passwd *pwbuf, char *buf, size_t
		//        buflen, struct passwd **pwbufp);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getpwuid_r")]
		private static extern int sys_getpwuid_r (uint uid, out _Passwd pwbuf, out IntPtr pwbufp);

		public static int getpwuid_r (uint uid, Passwd pwbuf, out Passwd pwbufp)
		{
			pwbufp = null;
			_Passwd passwd;
			IntPtr _pwbufp;
			int r = sys_getpwuid_r (uid, out passwd, out _pwbufp);
			if (r == 0 && _pwbufp != IntPtr.Zero) {
				CopyPasswd (pwbuf, ref passwd);
				pwbufp = pwbuf;
			}
			return r;
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getpwent")]
		private static extern int sys_getpwent (out _Passwd pwbuf);

		public static Passwd getpwent ()
		{
			_Passwd passwd;
			int r;
			lock (pwd_lock) {
				r = sys_getpwent (out passwd);
			}
			if (r != 0)
				return null;
			Passwd pw = new Passwd ();
			CopyPasswd (pw, ref passwd);
			return pw;
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_setpwent")]
		private static extern int sys_setpwent ();

		public static int setpwent ()
		{
			lock (pwd_lock) {
				return sys_setpwent ();
			}
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_endpwent")]
		private static extern int sys_endpwent ();

		public static int endpwent ()
		{
			lock (pwd_lock) {
				return sys_endpwent ();
			}
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_fgetpwent")]
		private static extern int sys_fgetpwent (IntPtr stream, out _Passwd pwbuf);

		public static Passwd fgetpwent (IntPtr stream)
		{
			_Passwd passwd;
			int r;
			lock (pwd_lock) {
				r = sys_fgetpwent (stream, out passwd);
			}
			if (r != 0)
				return null;
			Passwd pw = new Passwd ();
			CopyPasswd (pw, ref passwd);
			return pw;
		}
		#endregion

		#region <signal.h> Declarations
		//
		// <signal.h>
		//
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_psignal")]
		private static extern int psignal (int sig, string s);

		public static int psignal (Signum sig, string s)
		{
			int signum = NativeConvert.FromSignum (sig);
			return psignal (signum, s);
		}

		// kill(2)
		//    int kill(pid_t pid, int sig);
		[DllImport (LIBC, SetLastError=true, EntryPoint="kill")]
		private static extern int sys_kill (int pid, int sig);

		public static int kill (int pid, Signum sig)
		{
			int _sig = NativeConvert.FromSignum (sig);
			return sys_kill (pid, _sig);
		}

		private static object signal_lock = new object ();

		[DllImport (LIBC, SetLastError=true, EntryPoint="strsignal")]
		private static extern IntPtr sys_strsignal (int sig);

		public static string strsignal (Signum sig)
		{
			int s = NativeConvert.FromSignum (sig);
			lock (signal_lock) {
				IntPtr r = sys_strsignal (s);
				return UnixMarshal.PtrToString (r);
			}
		}

		// TODO: sigaction(2)
		// TODO: sigsuspend(2)
		// TODO: sigpending(2)

		#endregion

		#region <stdio.h> Declarations
		//
		// <stdio.h>
		//
		[DllImport (MPH, EntryPoint="Mono_Posix_Syscall_L_ctermid")]
		private static extern int _L_ctermid ();

		public static readonly int L_ctermid = _L_ctermid ();

		[DllImport (MPH, EntryPoint="Mono_Posix_Syscall_L_cuserid")]
		private static extern int _L_cuserid ();

		public static readonly int L_cuserid = _L_cuserid ();

		internal static object getlogin_lock = new object ();

		[DllImport (LIBC, SetLastError=true, EntryPoint="cuserid")]
		private static extern IntPtr sys_cuserid ([Out] StringBuilder @string);

		[Obsolete ("\"Nobody knows precisely what cuserid() does... " + 
				"DO NOT USE cuserid().\n" +
				"`string' must hold L_cuserid characters.  Use getlogin_r instead.")]
		public static string cuserid (StringBuilder @string)
		{
			if (@string.Capacity < L_cuserid) {
				throw new ArgumentOutOfRangeException ("string", "string.Capacity < L_cuserid");
			}
			lock (getlogin_lock) {
				IntPtr r = sys_cuserid (@string);
				return UnixMarshal.PtrToString (r);
			}
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int renameat (int olddirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string oldpath, int newdirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string newpath);
		#endregion

		#region <stdlib.h> Declarations
		//
		// <stdlib.h>
		//
		[DllImport (LIBC, SetLastError=true)]
		public static extern int mkstemp (StringBuilder template);

		[DllImport (LIBC, SetLastError=true, EntryPoint="mkdtemp")]
		private static extern IntPtr sys_mkdtemp (StringBuilder template);

		public static StringBuilder mkdtemp (StringBuilder template)
		{
			if (sys_mkdtemp (template) == IntPtr.Zero)
				return null;
			return template;
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int ttyslot ();

		[Obsolete ("This is insecure and should not be used", true)]
		public static int setkey (string key)
		{
			throw new SecurityException ("crypt(3) has been broken.  Use something more secure.");
		}

		#endregion

		#region <string.h> Declarations
		//
		// <string.h>
		//

		// strerror_r(3)
		//    int strerror_r(int errnum, char *buf, size_t n);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_strerror_r")]
		private static extern int sys_strerror_r (int errnum, 
				[Out] StringBuilder buf, ulong n);

		public static int strerror_r (Errno errnum, StringBuilder buf, ulong n)
		{
			int e = NativeConvert.FromErrno (errnum);
			return sys_strerror_r (e, buf, n);
		}

		public static int strerror_r (Errno errnum, StringBuilder buf)
		{
			return strerror_r (errnum, buf, (ulong) buf.Capacity);
		}

		#endregion

		#region <sys/epoll.h> Declarations

		public static int epoll_create (int size)
		{
			return sys_epoll_create (size);
		}

		public static int epoll_create (EpollFlags flags)
		{
			return sys_epoll_create1 (flags);
		}

		public static int epoll_ctl (int epfd, EpollOp op, int fd, EpollEvents events)
		{
			EpollEvent ee = new EpollEvent ();
			ee.events = events;
			ee.fd = fd;

			return epoll_ctl (epfd, op, fd, ref ee);
		}

		public static int epoll_wait (int epfd, EpollEvent [] events, int max_events, int timeout)
		{
			if (events.Length < max_events)
				throw new ArgumentOutOfRangeException ("events", "Must refer to at least 'max_events' elements.");

			return sys_epoll_wait (epfd, events, max_events, timeout);
		}

		[DllImport (LIBC, SetLastError=true, EntryPoint="epoll_create")]
		private static extern int sys_epoll_create (int size);

		[DllImport (LIBC, SetLastError=true, EntryPoint="epoll_create1")]
		private static extern int sys_epoll_create1 (EpollFlags flags);

		[DllImport (LIBC, SetLastError=true, EntryPoint="epoll_ctl")]
		public static extern int epoll_ctl (int epfd, EpollOp op, int fd, ref EpollEvent ee);

		[DllImport (LIBC, SetLastError=true, EntryPoint="epoll_wait")]
		private static extern int sys_epoll_wait (int epfd, [In,Out] EpollEvent [] ee, int maxevents, int timeout);
		#endregion
		
		#region <sys/mman.h> Declarations
		//
		// <sys/mman.h>
		//

		// posix_madvise(P)
		//    int posix_madvise(void *addr, size_t len, int advice);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_posix_madvise")]
		public static extern int posix_madvise (IntPtr addr, ulong len, 
			PosixMadviseAdvice advice);

		public static readonly IntPtr MAP_FAILED = unchecked((IntPtr)(-1));

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_mmap")]
		public static extern IntPtr mmap (IntPtr start, ulong length, 
				MmapProts prot, MmapFlags flags, int fd, long offset);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_munmap")]
		public static extern int munmap (IntPtr start, ulong length);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_mprotect")]
		public static extern int mprotect (IntPtr start, ulong len, MmapProts prot);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_msync")]
		public static extern int msync (IntPtr start, ulong len, MsyncFlags flags);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_mlock")]
		public static extern int mlock (IntPtr start, ulong len);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_munlock")]
		public static extern int munlock (IntPtr start, ulong len);

		[DllImport (LIBC, SetLastError=true, EntryPoint="mlockall")]
		private static extern int sys_mlockall (int flags);

		public static int mlockall (MlockallFlags flags)
		{
			int _flags = NativeConvert.FromMlockallFlags (flags);
			return sys_mlockall (_flags);
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int munlockall ();

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_mremap")]
		public static extern IntPtr mremap (IntPtr old_address, ulong old_size, 
				ulong new_size, MremapFlags flags);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_mincore")]
		public static extern int mincore (IntPtr start, ulong length, byte[] vec);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_remap_file_pages")]
		public static extern int remap_file_pages (IntPtr start, ulong size,
				MmapProts prot, long pgoff, MmapFlags flags);

		#endregion

		#region <sys/poll.h> Declarations
		//
		// <sys/poll.h> -- COMPLETE
		//
#pragma warning disable 649
		private struct _pollfd {
			public int fd;
			public short events;
			public short revents;
		}
#pragma warning restore 649

		[DllImport (LIBC, SetLastError=true, EntryPoint="poll")]
		private static extern int sys_poll ([In,Out] _pollfd[] ufds, uint nfds, int timeout);

		public static int poll (Pollfd [] fds, uint nfds, int timeout)
		{
			if (fds.Length < nfds)
				throw new ArgumentOutOfRangeException ("fds", "Must refer to at least `nfds' elements");

			_pollfd[] send = new _pollfd[nfds];

			for (int i = 0; i < send.Length; i++) {
				send [i].fd     = fds [i].fd;
				send [i].events = NativeConvert.FromPollEvents (fds [i].events);
			}

			int r = sys_poll (send, nfds, timeout);

			for (int i = 0; i < send.Length; i++) {
				fds [i].revents = NativeConvert.ToPollEvents (send [i].revents);
			}

			return r;
		}

		public static int poll (Pollfd [] fds, int timeout)
		{
			return poll (fds, (uint) fds.Length, timeout);
		}

		//
		// <sys/ptrace.h>
		//

		// TODO: ptrace(2)

		//
		// <sys/resource.h>
		//

		// TODO: setrlimit(2)
		// TODO: getrlimit(2)
		// TODO: getrusage(2)

		#endregion

		#region <sys/sendfile.h> Declarations
		//
		// <sys/sendfile.h> -- COMPLETE
		//

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_sendfile")]
		public static extern long sendfile (int out_fd, int in_fd, 
				ref long offset, ulong count);

		#endregion

		#region <sys/stat.h> Declarations
		//
		// <sys/stat.h>  -- COMPLETE
		//
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_stat")]
		public static extern int stat (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string file_name, out Stat buf);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_fstat")]
		public static extern int fstat (int filedes, out Stat buf);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_lstat")]
		public static extern int lstat (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string file_name, out Stat buf);

		// TODO:
		// S_ISDIR, S_ISCHR, S_ISBLK, S_ISREG, S_ISFIFO, S_ISLNK, S_ISSOCK
		// All take FilePermissions

		// chmod(2)
		//    int chmod(const char *path, mode_t mode);
		[DllImport (LIBC, SetLastError=true, EntryPoint="chmod")]
		private static extern int sys_chmod (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, uint mode);

		public static int chmod (string path, FilePermissions mode)
		{
			uint _mode = NativeConvert.FromFilePermissions (mode);
			return sys_chmod (path, _mode);
		}

		// fchmod(2)
		//    int chmod(int filedes, mode_t mode);
		[DllImport (LIBC, SetLastError=true, EntryPoint="fchmod")]
		private static extern int sys_fchmod (int filedes, uint mode);

		public static int fchmod (int filedes, FilePermissions mode)
		{
			uint _mode = NativeConvert.FromFilePermissions (mode);
			return sys_fchmod (filedes, _mode);
		}

		// umask(2)
		//    mode_t umask(mode_t mask);
		[DllImport (LIBC, SetLastError=true, EntryPoint="umask")]
		private static extern uint sys_umask (uint mask);

		public static FilePermissions umask (FilePermissions mask)
		{
			uint _mask = NativeConvert.FromFilePermissions (mask);
			uint r = sys_umask (_mask);
			return NativeConvert.ToFilePermissions (r);
		}

		// mkdir(2)
		//    int mkdir(const char *pathname, mode_t mode);
		[DllImport (LIBC, SetLastError=true, EntryPoint="mkdir")]
		private static extern int sys_mkdir (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string oldpath, uint mode);

		public static int mkdir (string oldpath, FilePermissions mode)
		{
			uint _mode = NativeConvert.FromFilePermissions (mode);
			return sys_mkdir (oldpath, _mode);
		}

		// mknod(2)
		//    int mknod (const char *pathname, mode_t mode, dev_t dev);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_mknod")]
		public static extern int mknod (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, FilePermissions mode, ulong dev);

		// mkfifo(3)
		//    int mkfifo(const char *pathname, mode_t mode);
		[DllImport (LIBC, SetLastError=true, EntryPoint="mkfifo")]
		private static extern int sys_mkfifo (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, uint mode);

		public static int mkfifo (string pathname, FilePermissions mode)
		{
			uint _mode = NativeConvert.FromFilePermissions (mode);
			return sys_mkfifo (pathname, _mode);
		}

		// fchmodat(2)
		//    int fchmodat(int dirfd, const char *pathname, mode_t mode, int flags);
		[DllImport (LIBC, SetLastError=true, EntryPoint="fchmodat")]
		private static extern int sys_fchmodat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, uint mode, int flags);

		public static int fchmodat (int dirfd, string pathname, FilePermissions mode, AtFlags flags)
		{
			uint _mode = NativeConvert.FromFilePermissions (mode);
			int _flags = NativeConvert.FromAtFlags (flags);
			return sys_fchmodat (dirfd, pathname, _mode, _flags);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_fstatat")]
		public static extern int fstatat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string file_name, out Stat buf, AtFlags flags);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_get_utime_now")]
		private static extern long get_utime_now ();

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_get_utime_omit")]
		private static extern long get_utime_omit ();

		public static readonly long UTIME_NOW = get_utime_now ();

		public static readonly long UTIME_OMIT = get_utime_omit ();

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_futimens")]
		private static extern int sys_futimens (int fd, Timespec[] times);

		public static int futimens (int fd, Timespec[] times)
		{
			if (times != null && times.Length != 2) {
				SetLastError (Errno.EINVAL);
				return -1;
			}
			return sys_futimens (fd, times);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_utimensat")]
		private static extern int sys_utimensat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, Timespec[] times, int flags);

		public static int utimensat (int dirfd, string pathname, Timespec[] times, AtFlags flags)
		{
			if (times != null && times.Length != 2) {
				SetLastError (Errno.EINVAL);
				return -1;
			}
			int _flags = NativeConvert.FromAtFlags (flags);
			return sys_utimensat (dirfd, pathname, times, _flags);
		}

		// mkdirat(2)
		//    int mkdirat(int dirfd, const char *pathname, mode_t mode);
		[DllImport (LIBC, SetLastError=true, EntryPoint="mkdirat")]
		private static extern int sys_mkdirat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string oldpath, uint mode);

		public static int mkdirat (int dirfd, string oldpath, FilePermissions mode)
		{
			uint _mode = NativeConvert.FromFilePermissions (mode);
			return sys_mkdirat (dirfd, oldpath, _mode);
		}

		// mknodat(2)
		//    int mknodat (int dirfd, const char *pathname, mode_t mode, dev_t dev);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_mknodat")]
		public static extern int mknodat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, FilePermissions mode, ulong dev);

		// mkfifoat(3)
		//    int mkfifoat(int dirfd, const char *pathname, mode_t mode);
		[DllImport (LIBC, SetLastError=true, EntryPoint="mkfifoat")]
		private static extern int sys_mkfifoat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, uint mode);

		public static int mkfifoat (int dirfd, string pathname, FilePermissions mode)
		{
			uint _mode = NativeConvert.FromFilePermissions (mode);
			return sys_mkfifoat (dirfd, pathname, _mode);
		}
		#endregion

		#region <sys/stat.h> Declarations
		//
		// <sys/statvfs.h>
		//

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_statvfs")]
		public static extern int statvfs (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, out Statvfs buf);

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_fstatvfs")]
		public static extern int fstatvfs (int fd, out Statvfs buf);

		#endregion

		#region <sys/time.h> Declarations
		//
		// <sys/time.h>
		//
		// TODO: adjtime(), getitimer(2), setitimer(2)

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_gettimeofday")]
		public static extern int gettimeofday (out Timeval tv, out Timezone tz);

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_gettimeofday")]
		private static extern int gettimeofday (out Timeval tv, IntPtr ignore);

		public static int gettimeofday (out Timeval tv)
		{
			return gettimeofday (out tv, IntPtr.Zero);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_gettimeofday")]
		private static extern int gettimeofday (IntPtr ignore, out Timezone tz);

		public static int gettimeofday (out Timezone tz)
		{
			return gettimeofday (IntPtr.Zero, out tz);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_settimeofday")]
		public static extern int settimeofday (ref Timeval tv, ref Timezone tz);

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_gettimeofday")]
		private static extern int settimeofday (ref Timeval tv, IntPtr ignore);

		public static int settimeofday (ref Timeval tv)
		{
			return settimeofday (ref tv, IntPtr.Zero);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_utimes")]
		private static extern int sys_utimes (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string filename, Timeval[] tvp);

		public static int utimes (string filename, Timeval[] tvp)
		{
			if (tvp != null && tvp.Length != 2) {
				SetLastError (Errno.EINVAL);
				return -1;
			}
			return sys_utimes (filename, tvp);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_lutimes")]
		private static extern int sys_lutimes (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string filename, Timeval[] tvp);

		public static int lutimes (string filename, Timeval[] tvp)
		{
			if (tvp != null && tvp.Length != 2) {
				SetLastError (Errno.EINVAL);
				return -1;
			}
			return sys_lutimes (filename, tvp);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_futimes")]
		private static extern int sys_futimes (int fd, Timeval[] tvp);

		public static int futimes (int fd, Timeval[] tvp)
		{
			if (tvp != null && tvp.Length != 2) {
				SetLastError (Errno.EINVAL);
				return -1;
			}
			return sys_futimes (fd, tvp);
		}

		#endregion

		//
		// <sys/timeb.h>
		//

		// TODO: ftime(3)

		//
		// <sys/times.h>
		//

		// TODO: times(2)

		//
		// <sys/utsname.h>
		//

		[Map]
		private struct _Utsname
		{
			public IntPtr sysname;
			public IntPtr nodename;
			public IntPtr release;
			public IntPtr version;
			public IntPtr machine;
			public IntPtr domainname;
			public IntPtr _buf_;
		}

		private static void CopyUtsname (ref Utsname to, ref _Utsname from)
		{
			try {
				to = new Utsname ();
				to.sysname     = UnixMarshal.PtrToString (from.sysname);
				to.nodename    = UnixMarshal.PtrToString (from.nodename);
				to.release     = UnixMarshal.PtrToString (from.release);
				to.version     = UnixMarshal.PtrToString (from.version);
				to.machine     = UnixMarshal.PtrToString (from.machine);
				to.domainname  = UnixMarshal.PtrToString (from.domainname);
			}
			finally {
				Stdlib.free (from._buf_);
				from._buf_ = IntPtr.Zero;
			}
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_uname")]
		private static extern int sys_uname (out _Utsname buf);

		public static int uname (out Utsname buf)
		{
			_Utsname _buf;
			int r = sys_uname (out _buf);
			buf = new Utsname ();
			if (r == 0) {
				CopyUtsname (ref buf, ref _buf);
			}
			return r;
		}

		#region <sys/wait.h> Declarations
		//
		// <sys/wait.h>
		//

		// wait(2)
		//    pid_t wait(int *status);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int wait (out int status);

		// waitpid(2)
		//    pid_t waitpid(pid_t pid, int *status, int options);
		[DllImport (LIBC, SetLastError=true)]
		private static extern int waitpid (int pid, out int status, int options);

		public static int waitpid (int pid, out int status, WaitOptions options)
		{
			int _options = NativeConvert.FromWaitOptions (options);
			return waitpid (pid, out status, _options);
		}

		[DllImport (MPH, EntryPoint="Mono_Posix_Syscall_WIFEXITED")]
		private static extern int _WIFEXITED (int status);

		public static bool WIFEXITED (int status)
		{
			return _WIFEXITED (status) != 0;
		}

		[DllImport (MPH, EntryPoint="Mono_Posix_Syscall_WEXITSTATUS")]
		public static extern int WEXITSTATUS (int status);

		[DllImport (MPH, EntryPoint="Mono_Posix_Syscall_WIFSIGNALED")]
		private static extern int _WIFSIGNALED (int status);

		public static bool WIFSIGNALED (int status)
		{
			return _WIFSIGNALED (status) != 0;
		}

		[DllImport (MPH, EntryPoint="Mono_Posix_Syscall_WTERMSIG")]
		private static extern int _WTERMSIG (int status);

		public static Signum WTERMSIG (int status)
		{
			int r = _WTERMSIG (status);
			return NativeConvert.ToSignum (r);
		}

		[DllImport (MPH, EntryPoint="Mono_Posix_Syscall_WIFSTOPPED")]
		private static extern int _WIFSTOPPED (int status);

		public static bool WIFSTOPPED (int status)
		{
			return _WIFSTOPPED (status) != 0;
		}

		[DllImport (MPH, EntryPoint="Mono_Posix_Syscall_WSTOPSIG")]
		private static extern int _WSTOPSIG (int status);

		public static Signum WSTOPSIG (int status)
		{
			int r = _WSTOPSIG (status);
			return NativeConvert.ToSignum (r);
		}

		//
		// <termios.h>
		//

		#endregion

		#region <syslog.h> Declarations
		//
		// <syslog.h>
		//

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_openlog")]
		private static extern int sys_openlog (IntPtr ident, int option, int facility);

		public static int openlog (IntPtr ident, SyslogOptions option, 
				SyslogFacility defaultFacility)
		{
			int _option   = NativeConvert.FromSyslogOptions (option);
			int _facility = NativeConvert.FromSyslogFacility (defaultFacility);

			return sys_openlog (ident, _option, _facility);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_syslog")]
		private static extern int sys_syslog (int priority, string message);

		public static int syslog (SyslogFacility facility, SyslogLevel level, string message)
		{
			int _facility = NativeConvert.FromSyslogFacility (facility);
			int _level = NativeConvert.FromSyslogLevel (level);
			return sys_syslog (_facility | _level, GetSyslogMessage (message));
		}

		public static int syslog (SyslogLevel level, string message)
		{
			int _level = NativeConvert.FromSyslogLevel (level);
			return sys_syslog (_level, GetSyslogMessage (message));
		}

		private static string GetSyslogMessage (string message)
		{
			return UnixMarshal.EscapeFormatString (message, new char[]{'m'});
		}

#if !NETSTANDARD2_0
		[Obsolete ("Not necessarily portable due to cdecl restrictions.\n" +
				"Use syslog(SyslogFacility, SyslogLevel, string) instead.")]
		public static int syslog (SyslogFacility facility, SyslogLevel level, 
				string format, params object[] parameters)
		{
			int _facility = NativeConvert.FromSyslogFacility (facility);
			int _level = NativeConvert.FromSyslogLevel (level);

			object[] _parameters = new object[checked(parameters.Length+2)];
			_parameters [0] = _facility | _level;
			_parameters [1] = format;
			Array.Copy (parameters, 0, _parameters, 2, parameters.Length);
			return (int) XPrintfFunctions.syslog (_parameters);
		}

		[Obsolete ("Not necessarily portable due to cdecl restrictions.\n" +
				"Use syslog(SyslogLevel, string) instead.")]
		public static int syslog (SyslogLevel level, string format, 
				params object[] parameters)
		{
			int _level = NativeConvert.FromSyslogLevel (level);

			object[] _parameters = new object[checked(parameters.Length+2)];
			_parameters [0] = _level;
			_parameters [1] = format;
			Array.Copy (parameters, 0, _parameters, 2, parameters.Length);
			return (int) XPrintfFunctions.syslog (_parameters);
		}
#endif

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_closelog")]
		public static extern int closelog ();

		[DllImport (LIBC, SetLastError=true, EntryPoint="setlogmask")]
		private static extern int sys_setlogmask (int mask);

		public static int setlogmask (SyslogLevel mask)
		{
			int _mask = NativeConvert.FromSyslogLevel (mask);
			return sys_setlogmask (_mask);
		}

		#endregion

		#region <time.h> Declarations

		//
		// <time.h>
		//

		// nanosleep(2)
		//    int nanosleep(const struct timespec *req, struct timespec *rem);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_nanosleep")]
		public static extern int nanosleep (ref Timespec req, ref Timespec rem);

		// stime(2)
		//    int stime(time_t *t);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_stime")]
		public static extern int stime (ref long t);

		// time(2)
		//    time_t time(time_t *t);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_time")]
		public static extern long time (out long t);

		//
		// <ulimit.h>
		//

		// TODO: ulimit(3)

		#endregion

		#region <unistd.h> Declarations
		//
		// <unistd.h>
		//
		// TODO: euidaccess(), usleep(3), get_current_dir_name(), group_member(),
		//       other TODOs listed below.

		[DllImport (LIBC, SetLastError=true, EntryPoint="access")]
		private static extern int sys_access (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, int mode);

		public static int access (string pathname, AccessModes mode)
		{
			int _mode = NativeConvert.FromAccessModes (mode);
			return sys_access (pathname, _mode);
		}

		// lseek(2)
		//    off_t lseek(int filedes, off_t offset, int whence);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_lseek")]
		private static extern long sys_lseek (int fd, long offset, int whence);

		public static long lseek (int fd, long offset, SeekFlags whence)
		{
			short _whence = NativeConvert.FromSeekFlags (whence);
			return sys_lseek (fd, offset, _whence);
		}

    [DllImport (LIBC, SetLastError=true)]
		public static extern int close (int fd);

		// read(2)
		//    ssize_t read(int fd, void *buf, size_t count);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_read")]
		public static extern long read (int fd, IntPtr buf, ulong count);

		public static unsafe long read (int fd, void *buf, ulong count)
		{
			return read (fd, (IntPtr) buf, count);
		}

		// write(2)
		//    ssize_t write(int fd, const void *buf, size_t count);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_write")]
		public static extern long write (int fd, IntPtr buf, ulong count);

		public static unsafe long write (int fd, void *buf, ulong count)
		{
			return write (fd, (IntPtr) buf, count);
		}

		// pread(2)
		//    ssize_t pread(int fd, void *buf, size_t count, off_t offset);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_pread")]
		public static extern long pread (int fd, IntPtr buf, ulong count, long offset);

		public static unsafe long pread (int fd, void *buf, ulong count, long offset)
		{
			return pread (fd, (IntPtr) buf, count, offset);
		}

		// pwrite(2)
		//    ssize_t pwrite(int fd, const void *buf, size_t count, off_t offset);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_pwrite")]
		public static extern long pwrite (int fd, IntPtr buf, ulong count, long offset);

		public static unsafe long pwrite (int fd, void *buf, ulong count, long offset)
		{
			return pwrite (fd, (IntPtr) buf, count, offset);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_pipe")]
		public static extern int pipe (out int reading, out int writing);

		public static int pipe (int[] filedes)
		{
			if (filedes == null || filedes.Length != 2) {
				// TODO: set errno
				return -1;
			}
			int reading, writing;
			int r = pipe (out reading, out writing);
			filedes[0] = reading;
			filedes[1] = writing;
			return r;
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern uint alarm (uint seconds);

		[DllImport (LIBC, SetLastError=true)]
		public static extern uint sleep (uint seconds);

		[DllImport (LIBC, SetLastError=true)]
		public static extern uint ualarm (uint usecs, uint interval);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int pause ();

		// chown(2)
		//    int chown(const char *path, uid_t owner, gid_t group);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int chown (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, int owner, int group);

		// fchown(2)
		//    int fchown(int fd, uid_t owner, gid_t group);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int fchown (int fd, int owner, int group);

		// lchown(2)
		//    int lchown(const char *path, uid_t owner, gid_t group);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int lchown (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, int owner, int group);

		#region UInt32 overloads for initial incorrect implementation
		[Obsolete("Use Int32 overload")]
		[DllImport (LIBC, SetLastError=true)]
		public static extern int chown (
			[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
			string path, uint owner, uint group);

		[Obsolete("Use Int32 overload")]
		[DllImport (LIBC, SetLastError=true)]
		public static extern int fchown (int fd, uint owner, uint group);

		[Obsolete("Use Int32 overload")]
		[DllImport (LIBC, SetLastError=true)]
		public static extern int lchown (
			[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
			string path, uint owner, uint group);
		#endregion

		[DllImport (LIBC, SetLastError=true)]
		public static extern int chdir (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int fchdir (int fd);

		// getcwd(3)
		//    char *getcwd(char *buf, size_t size);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getcwd")]
		public static extern IntPtr getcwd ([Out] StringBuilder buf, ulong size);

		public static StringBuilder getcwd (StringBuilder buf)
		{
			getcwd (buf, (ulong) buf.Capacity);
			return buf;
		}

		// getwd(2) is deprecated; don't expose it.

		[DllImport (LIBC, SetLastError=true)]
		public static extern int dup (int fd);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int dup2 (int fd, int fd2);

		// TODO: does Mono marshal arrays properly?
		[DllImport (LIBC, SetLastError=true)]
		public static extern int execve (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, string[] argv, string[] envp);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int fexecve (int fd, string[] argv, string[] envp);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int execv (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, string[] argv);

		// TODO: execle, execl, execlp
		[DllImport (LIBC, SetLastError=true)]
		public static extern int execvp (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, string[] argv);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int nice (int inc);

		[DllImport (LIBC, SetLastError=true)]
		[CLSCompliant (false)]
		public static extern int _exit (int status);

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_fpathconf")]
		public static extern long fpathconf (int filedes, PathconfName name, Errno defaultError);

		public static long fpathconf (int filedes, PathconfName name)
		{
			return fpathconf (filedes, name, (Errno) 0);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_pathconf")]
		public static extern long pathconf (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, PathconfName name, Errno defaultError);

		public static long pathconf (string path, PathconfName name)
		{
			return pathconf (path, name, (Errno) 0);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_sysconf")]
		public static extern long sysconf (SysconfName name, Errno defaultError);

		public static long sysconf (SysconfName name)
		{
			return sysconf (name, (Errno) 0);
		}

		// confstr(3)
		//    size_t confstr(int name, char *buf, size_t len);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_confstr")]
		public static extern ulong confstr (ConfstrName name, [Out] StringBuilder buf, ulong len);

		// getpid(2)
		//    pid_t getpid(void);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int getpid ();

		// getppid(2)
		//    pid_t getppid(void);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int getppid ();

		// setpgid(2)
		//    int setpgid(pid_t pid, pid_t pgid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setpgid (int pid, int pgid);

		// getpgid(2)
		//    pid_t getpgid(pid_t pid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int getpgid (int pid);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int setpgrp ();

		// getpgrp(2)
		//    pid_t getpgrp(void);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int getpgrp ();

		// setsid(2)
		//    pid_t setsid(void);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setsid ();

		// getsid(2)
		//    pid_t getsid(pid_t pid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int getsid (int pid);

		// getuid(2)
		//    uid_t getuid(void);
		[DllImport (LIBC, SetLastError=true)]
		public static extern uint getuid ();

		// geteuid(2)
		//    uid_t geteuid(void);
		[DllImport (LIBC, SetLastError=true)]
		public static extern uint geteuid ();

		// getgid(2)
		//    gid_t getgid(void);
		[DllImport (LIBC, SetLastError=true)]
		public static extern uint getgid ();

		// getegid(2)
		//    gid_t getgid(void);
		[DllImport (LIBC, SetLastError=true)]
		public static extern uint getegid ();

		// getgroups(2)
		//    int getgroups(int size, gid_t list[]);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int getgroups (int size, uint[] list);

		public static int getgroups (uint[] list)
		{
			return getgroups (list.Length, list);
		}

		// setuid(2)
		//    int setuid(uid_t uid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setuid (uint uid);

		// setreuid(2)
		//    int setreuid(uid_t ruid, uid_t euid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setreuid (uint ruid, uint euid);

		// setregid(2)
		//    int setregid(gid_t ruid, gid_t euid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setregid (uint rgid, uint egid);

		// seteuid(2)
		//    int seteuid(uid_t euid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int seteuid (uint euid);

		// setegid(2)
		//    int setegid(gid_t euid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setegid (uint uid);

		// setgid(2)
		//    int setgid(gid_t gid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setgid (uint gid);

		// getresuid(2)
		//    int getresuid(uid_t *ruid, uid_t *euid, uid_t *suid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int getresuid (out uint ruid, out uint euid, out uint suid);

		// getresgid(2)
		//    int getresgid(gid_t *ruid, gid_t *euid, gid_t *suid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int getresgid (out uint rgid, out uint egid, out uint sgid);

		// setresuid(2)
		//    int setresuid(uid_t ruid, uid_t euid, uid_t suid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setresuid (uint ruid, uint euid, uint suid);

		// setresgid(2)
		//    int setresgid(gid_t ruid, gid_t euid, gid_t suid);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int setresgid (uint rgid, uint egid, uint sgid);

#if false
		// fork(2)
		//    pid_t fork(void);
		[DllImport (LIBC, SetLastError=true)]
		[Obsolete ("DO NOT directly call fork(2); it bypasses essential " + 
				"shutdown code.\nUse System.Diagnostics.Process instead")]
		private static extern int fork ();

		// vfork(2)
		//    pid_t vfork(void);
		[DllImport (LIBC, SetLastError=true)]
		[Obsolete ("DO NOT directly call vfork(2); it bypasses essential " + 
				"shutdown code.\nUse System.Diagnostics.Process instead")]
		private static extern int vfork ();
#endif

		private static object tty_lock = new object ();

		[DllImport (LIBC, SetLastError=true, EntryPoint="ttyname")]
		private static extern IntPtr sys_ttyname (int fd);

		public static string ttyname (int fd)
		{
			lock (tty_lock) {
				IntPtr r = sys_ttyname (fd);
				return UnixMarshal.PtrToString (r);
			}
		}

		// ttyname_r(3)
		//    int ttyname_r(int fd, char *buf, size_t buflen);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_ttyname_r")]
		public static extern int ttyname_r (int fd, [Out] StringBuilder buf, ulong buflen);

		public static int ttyname_r (int fd, StringBuilder buf)
		{
			return ttyname_r (fd, buf, (ulong) buf.Capacity);
		}

		[DllImport (LIBC, EntryPoint="isatty")]
		private static extern int sys_isatty (int fd);

		public static bool isatty (int fd)
		{
			return sys_isatty (fd) == 1;
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int link (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string oldpath, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string newpath);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int symlink (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string oldpath, 
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string newpath);

		delegate long DoReadlinkFun (byte[] target);

		// Helper function for readlink(string, StringBuilder) and readlinkat (int, string, StringBuilder)
		static int ReadlinkIntoStringBuilder (DoReadlinkFun doReadlink, [Out] StringBuilder buf, ulong bufsiz)
		{
			// bufsiz > int.MaxValue can't work because StringBuilder can store only int.MaxValue chars
			int bufsizInt = checked ((int) bufsiz);
			var target = new byte [bufsizInt];

			var r = doReadlink (target);
			if (r < 0)
				return checked ((int) r);

			buf.Length = 0;
			var chars = UnixEncoding.Instance.GetChars (target, 0, checked ((int) r));
			// Make sure that at more bufsiz chars are written
			buf.Append (chars, 0, System.Math.Min (bufsizInt, chars.Length));
			if (r == bufsizInt) {
				// may not have read full contents; fill 'buf' so that caller can properly check
				buf.Append (new string ('\x00', bufsizInt - buf.Length));
			}
			return buf.Length;
		}

		// readlink(2)
		//    ssize_t readlink(const char *path, char *buf, size_t bufsize);
		public static int readlink (string path, [Out] StringBuilder buf, ulong bufsiz)
		{
			return ReadlinkIntoStringBuilder (target => readlink (path, target), buf, bufsiz);
		}

		public static int readlink (string path, [Out] StringBuilder buf)
		{
			return readlink (path, buf, (ulong) buf.Capacity);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_readlink")]
		private static extern long readlink (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, byte[] buf, ulong bufsiz);

		public static long readlink (string path, byte[] buf)
		{
			return readlink (path, buf, (ulong) buf.LongLength);
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int unlink (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int rmdir (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname);

		// tcgetpgrp(3)
		//    pid_t tcgetpgrp(int fd);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int tcgetpgrp (int fd);

		// tcsetpgrp(3)
		//    int tcsetpgrp(int fd, pid_t pgrp);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int tcsetpgrp (int fd, int pgrp);

		[DllImport (LIBC, SetLastError=true, EntryPoint="getlogin")]
		private static extern IntPtr sys_getlogin ();

		public static string getlogin ()
		{
			lock (getlogin_lock) {
				IntPtr r = sys_getlogin ();
				return UnixMarshal.PtrToString (r);
			}
		}

		// getlogin_r(3)
		//    int getlogin_r(char *buf, size_t bufsize);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getlogin_r")]
		public static extern int getlogin_r ([Out] StringBuilder name, ulong bufsize);

		public static int getlogin_r (StringBuilder name)
		{
			return getlogin_r (name, (ulong) name.Capacity);
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int setlogin (string name);

		// gethostname(2)
		//    int gethostname(char *name, size_t len);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_gethostname")]
		public static extern int gethostname ([Out] StringBuilder name, ulong len);

		public static int gethostname (StringBuilder name)
		{
			return gethostname (name, (ulong) name.Capacity);
		}

		// sethostname(2)
		//    int gethostname(const char *name, size_t len);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_sethostname")]
		public static extern int sethostname (string name, ulong len);

		public static int sethostname (string name)
		{
			return sethostname (name, (ulong) name.Length);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_gethostid")]
		public static extern long gethostid ();

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_sethostid")]
		public static extern int sethostid (long hostid);

		// getdomainname(2)
		//    int getdomainname(char *name, size_t len);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_getdomainname")]
		public static extern int getdomainname ([Out] StringBuilder name, ulong len);

		public static int getdomainname (StringBuilder name)
		{
			return getdomainname (name, (ulong) name.Capacity);
		}

		// setdomainname(2)
		//    int setdomainname(const char *name, size_t len);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_setdomainname")]
		public static extern int setdomainname (string name, ulong len);

		public static int setdomainname (string name)
		{
			return setdomainname (name, (ulong) name.Length);
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int vhangup ();

		// Revoke doesn't appear to be POSIX.  Include it?
		[DllImport (LIBC, SetLastError=true)]
		public static extern int revoke (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string file);

		// TODO: profil?  It's not POSIX.

		[DllImport (LIBC, SetLastError=true)]
		public static extern int acct (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string filename);

		[DllImport (LIBC, SetLastError=true, EntryPoint="getusershell")]
		private static extern IntPtr sys_getusershell ();

		internal static object usershell_lock = new object ();

		public static string getusershell ()
		{
			lock (usershell_lock) {
				IntPtr r = sys_getusershell ();
				return UnixMarshal.PtrToString (r);
			}
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_setusershell")]
		private static extern int sys_setusershell ();

		public static int setusershell ()
		{
			lock (usershell_lock) {
				return sys_setusershell ();
			}
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_endusershell")]
		private static extern int sys_endusershell ();

		public static int endusershell ()
		{
			lock (usershell_lock) {
				return sys_endusershell ();
			}
		}

#if false
		[DllImport (LIBC, SetLastError=true)]
		private static extern int daemon (int nochdir, int noclose);

		// this implicitly forks, and thus isn't safe.
		private static int daemon (bool nochdir, bool noclose)
		{
			return daemon (nochdir ? 1 : 0, noclose ? 1 : 0);
		}
#endif

		[DllImport (LIBC, SetLastError=true)]
		public static extern int chroot (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path);

		// skipping getpass(3) as the man page states:
		//   This function is obsolete.  Do not use it.

		[DllImport (LIBC, SetLastError=true)]
		public static extern int fsync (int fd);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int fdatasync (int fd);

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_sync")]
		public static extern int sync ();

		[DllImport (LIBC, SetLastError=true)]
		[Obsolete ("Dropped in POSIX 1003.1-2001.  " +
				"Use Syscall.sysconf (SysconfName._SC_PAGESIZE).")]
		public static extern int getpagesize ();

		// truncate(2)
		//    int truncate(const char *path, off_t length);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_truncate")]
		public static extern int truncate (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string path, long length);

		// ftruncate(2)
		//    int ftruncate(int fd, off_t length);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_ftruncate")]
		public static extern int ftruncate (int fd, long length);

		[DllImport (LIBC, SetLastError=true)]
		public static extern int getdtablesize ();

		[DllImport (LIBC, SetLastError=true)]
		public static extern int brk (IntPtr end_data_segment);

		[DllImport (LIBC, SetLastError=true)]
		public static extern IntPtr sbrk (IntPtr increment);

		// TODO: syscall(2)?
		// Probably safer to skip entirely.

		// lockf(3)
		//    int lockf(int fd, int cmd, off_t len);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_lockf")]
		public static extern int lockf (int fd, LockfCommand cmd, long len);

		[Obsolete ("This is insecure and should not be used", true)]
		public static string crypt (string key, string salt)
		{
			throw new SecurityException ("crypt(3) has been broken.  Use something more secure.");
		}

		[Obsolete ("This is insecure and should not be used", true)]
		public static int encrypt (byte[] block, bool decode)
		{
			throw new SecurityException ("crypt(3) has been broken.  Use something more secure.");
		}

		// swab(3)
		//    void swab(const void *from, void *to, ssize_t n);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_swab")]
		public static extern int swab (IntPtr from, IntPtr to, long n);

		public static unsafe void swab (void* from, void* to, long n)
		{
			swab ((IntPtr) from, (IntPtr) to, n);
		}

		[DllImport (LIBC, SetLastError=true, EntryPoint="faccessat")]
		private static extern int sys_faccessat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, int mode, int flags);

		public static int faccessat (int dirfd, string pathname, AccessModes mode, AtFlags flags)
		{
			int _mode = NativeConvert.FromAccessModes (mode);
			int _flags = NativeConvert.FromAtFlags (flags);
			return sys_faccessat (dirfd, pathname, _mode, _flags);
		}

		// fchownat(2)
		//    int fchownat(int dirfd, const char *path, uid_t owner, gid_t group, int flags);
		[DllImport (LIBC, SetLastError=true, EntryPoint="fchownat")]
		private static extern int sys_fchownat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, int owner, int group, int flags);

		public static int fchownat (int dirfd, string pathname, int owner, int group, AtFlags flags)
		{
			int _flags = NativeConvert.FromAtFlags (flags);
			return sys_fchownat (dirfd, pathname, owner, group, _flags);
		}

		#region UInt32 overloads for initial incorrect implementation
		[Obsolete("Use Int32 overload")]
		[DllImport (LIBC, SetLastError=true, EntryPoint="fchownat")]
		private static extern int sys_fchownat (int dirfd,
			[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
			string pathname, uint owner, uint group, int flags);

		[Obsolete("Use Int32 overload")]
		public static int fchownat (int dirfd, string pathname, uint owner, uint group, AtFlags flags)
		{
			int _flags = NativeConvert.FromAtFlags (flags);
			return sys_fchownat (dirfd, pathname, owner, group, _flags);
		}
		#endregion

		[DllImport (LIBC, SetLastError=true, EntryPoint="linkat")]
		private static extern int sys_linkat (int olddirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string oldpath, int newdirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string newpath, int flags);

		public static int linkat (int olddirfd, string oldpath, int newdirfd, string newpath, AtFlags flags)
		{
			int _flags = NativeConvert.FromAtFlags (flags);
			return sys_linkat (olddirfd, oldpath, newdirfd, newpath, _flags);
		}

		// readlinkat(2)
		//    ssize_t readlinkat(int dirfd, const char *pathname, char *buf, size_t bufsize);
		public static int readlinkat (int dirfd, string pathname, [Out] StringBuilder buf, ulong bufsiz)
		{
			return ReadlinkIntoStringBuilder (target => readlinkat (dirfd, pathname, target), buf, bufsiz);
		}

		public static int readlinkat (int dirfd, string pathname, [Out] StringBuilder buf)
		{
			return readlinkat (dirfd, pathname, buf, (ulong) buf.Capacity);
		}

		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_readlinkat")]
		private static extern long readlinkat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, byte[] buf, ulong bufsiz);

		public static long readlinkat (int dirfd, string pathname, byte[] buf)
		{
			return readlinkat (dirfd, pathname, buf, (ulong) buf.LongLength);
		}

		[DllImport (LIBC, SetLastError=true)]
		public static extern int symlinkat (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string oldpath, int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string newpath);

		[DllImport (LIBC, SetLastError=true, EntryPoint="unlinkat")]
		private static extern int sys_unlinkat (int dirfd,
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string pathname, int flags);

		public static int unlinkat (int dirfd, string pathname, AtFlags flags)
		{
			int _flags = NativeConvert.FromAtFlags (flags);
			return sys_unlinkat (dirfd, pathname, _flags);
		}
		#endregion

		#region <utime.h> Declarations
		//
		// <utime.h>  -- COMPLETE
		//

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_utime")]
		private static extern int sys_utime (
				[MarshalAs (UnmanagedType.CustomMarshaler, MarshalTypeRef=typeof(FileNameMarshaler))]
				string filename, ref Utimbuf buf, int use_buf);

		public static int utime (string filename, ref Utimbuf buf)
		{
			return sys_utime (filename, ref buf, 1);
		}

		public static int utime (string filename)
		{
			Utimbuf buf = new Utimbuf ();
			return sys_utime (filename, ref buf, 0);
		}
		#endregion

		#region <sys/uio.h> Declarations
		//
		// <sys/uio.h> -- COMPLETE
		//

		// readv(2)
		//    ssize_t readv(int fd, const struct iovec *iov, int iovcnt);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_readv")]
		private static extern long sys_readv (int fd, Iovec[] iov, int iovcnt);

		public static long readv (int fd, Iovec[] iov)
		{
			return sys_readv (fd, iov, iov.Length);
		}

		// writev(2)
		//    ssize_t writev(int fd, const struct iovec *iov, int iovcnt);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_writev")]
		private static extern long sys_writev (int fd, Iovec[] iov, int iovcnt);

		public static long writev (int fd, Iovec[] iov)
		{
			return sys_writev (fd, iov, iov.Length);
		}

		// preadv(2)
		//    ssize_t preadv(int fd, const struct iovec *iov, int iovcnt, off_t offset);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_preadv")]
		private static extern long sys_preadv (int fd, Iovec[] iov, int iovcnt, long offset);

		public static long preadv (int fd, Iovec[] iov, long offset)
		{
			return sys_preadv (fd, iov, iov.Length, offset);
		}

		// pwritev(2)
		//    ssize_t pwritev(int fd, const struct iovec *iov, int iovcnt, off_t offset);
		[DllImport (MPH, SetLastError=true,
				EntryPoint="Mono_Posix_Syscall_pwritev")]
		private static extern long sys_pwritev (int fd, Iovec[] iov, int iovcnt, long offset);

		public static long pwritev (int fd, Iovec[] iov, long offset)
		{
			return sys_pwritev (fd, iov, iov.Length, offset);
		}
		#endregion

		#region <arpa/inet.h> Declarations
		//
		// <arpa/inet.h>
		//

		// htonl(3)
		//    uint32_t htonl(uint32_t hostlong);
		[DllImport (LIBC)]
		public static extern uint htonl(uint hostlong);

		// htons(3)
		//    uint16_t htons(uint16_t hostshort);
		[DllImport (LIBC)]
		public static extern ushort htons(ushort hostshort);

		// ntohl(3)
		//    uint32_t ntohl(uint32_t netlong);
		[DllImport (LIBC)]
		public static extern uint ntohl(uint netlong);

		// ntohs(3)
		//    uint16_t ntohs(uint16_t netshort);
		[DllImport (LIBC)]
		public static extern ushort ntohs(ushort netshort);

		#endregion

		#region <socket.h> Declarations
		//
		// <socket.h> -- COMPLETE
		//

		// socket(2)
		//    int socket(int domain, int type, int protocol);
		[DllImport (LIBC, SetLastError=true, 
				EntryPoint="socket")]
		static extern int sys_socket (int domain, int type, int protocol);

		public static int socket (UnixAddressFamily domain, UnixSocketType type, UnixSocketFlags flags, UnixSocketProtocol protocol)
		{
			var _domain = NativeConvert.FromUnixAddressFamily (domain);
			var _type = NativeConvert.FromUnixSocketType (type);
			var _flags = NativeConvert.FromUnixSocketFlags (flags);
			// protocol == 0 is a special case (uses default protocol)
			var _protocol = protocol == 0 ? 0 : NativeConvert.FromUnixSocketProtocol (protocol);

			return sys_socket (_domain, _type | _flags, _protocol);
		}

		public static int socket (UnixAddressFamily domain, UnixSocketType type, UnixSocketProtocol protocol)
		{
			return socket (domain, type, 0, protocol);
		}

		// socketpair(2)
		//    int socketpair(int domain, int type, int protocol, int sv[2]);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_socketpair")]
		static extern int sys_socketpair (int domain, int type, int protocol, out int socket1, out int socket2);

		public static int socketpair (UnixAddressFamily domain, UnixSocketType type, UnixSocketFlags flags, UnixSocketProtocol protocol, out int socket1, out int socket2)
		{
			var _domain = NativeConvert.FromUnixAddressFamily (domain);
			var _type = NativeConvert.FromUnixSocketType (type);
			var _flags = NativeConvert.FromUnixSocketFlags (flags);
			// protocol == 0 is a special case (uses default protocol)
			var _protocol = protocol == 0 ? 0 : NativeConvert.FromUnixSocketProtocol (protocol);

			return sys_socketpair (_domain, _type | _flags, _protocol, out socket1, out socket2);
		}

		public static int socketpair (UnixAddressFamily domain, UnixSocketType type, UnixSocketProtocol protocol, out int socket1, out int socket2)
		{
			return socketpair (domain, type, 0, protocol, out socket1, out socket2);
		}

		// sockatmark(2)
		//    int sockatmark(int sockfd);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int sockatmark (int socket);

		// listen(2)
		//    int listen(int sockfd, int backlog);
		[DllImport (LIBC, SetLastError=true)]
		public static extern int listen (int socket, int backlog);

		// getsockopt(2)
		//    int getsockopt(int sockfd, int level, int optname, void *optval, socklen_t *optlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_getsockopt")]
		static extern unsafe int sys_getsockopt (int socket, int level, int option_name, void *option_value, ref long option_len);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_getsockopt_timeval")]
		static extern unsafe int sys_getsockopt_timeval (int socket, int level, int option_name, out Timeval option_value);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_getsockopt_linger")]
		static extern unsafe int sys_getsockopt_linger (int socket, int level, int option_name, out Linger option_value);

		public static unsafe int getsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, void *option_value, ref long option_len)
		{
			var _level = NativeConvert.FromUnixSocketProtocol (level);
			var _option_name = NativeConvert.FromUnixSocketOptionName (option_name);
			return sys_getsockopt (socket, _level, _option_name, option_value, ref option_len);
		}

		public static unsafe int getsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, IntPtr option_value, ref long option_len)
		{
			return getsockopt (socket, level, option_name, (void*) option_value, ref option_len);
		}

		public static unsafe int getsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, out int option_value)
		{
			int value;
			long size = sizeof (int);
			int ret = getsockopt (socket, level, option_name, &value, ref size);
			if (ret != -1 && size != sizeof (int)) {
				SetLastError (Errno.EINVAL);
				ret = -1;
			}
			option_value = value;
			return ret;
		}

		public static unsafe int getsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, byte[] option_value, ref long option_len)
		{
			if (option_len > (option_value == null ? 0 : option_value.Length))
				throw new ArgumentOutOfRangeException ("option_len", "option_len > (option_value == null ? 0 : option_value.Length)");
			fixed (byte* ptr = option_value)
				return getsockopt (socket, level, option_name, ptr, ref option_len);
		}

		public static unsafe int getsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, out Timeval option_value)
		{
			var _level = NativeConvert.FromUnixSocketProtocol (level);
			var _option_name = NativeConvert.FromUnixSocketOptionName (option_name);
			return sys_getsockopt_timeval (socket, _level, _option_name, out option_value);
		}

		public static unsafe int getsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, out Linger option_value)
		{
			var _level = NativeConvert.FromUnixSocketProtocol (level);
			var _option_name = NativeConvert.FromUnixSocketOptionName (option_name);
			return sys_getsockopt_linger (socket, _level, _option_name, out option_value);
		}

		// setsockopt(2)
		//    int setsockopt(int sockfd, int level, int optname, const void *optval, socklen_t optlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_setsockopt")]
		static extern unsafe int sys_setsockopt (int socket, int level, int option_name, void *option_value, long option_len);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_setsockopt_timeval")]
		static extern unsafe int sys_setsockopt_timeval (int socket, int level, int option_name, ref Timeval option_value);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_setsockopt_linger")]
		static extern unsafe int sys_setsockopt_linger (int socket, int level, int option_name, ref Linger option_value);

		public static unsafe int setsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, void *option_value, long option_len)
		{
			var _level = NativeConvert.FromUnixSocketProtocol (level);
			var _option_name = NativeConvert.FromUnixSocketOptionName (option_name);
			return sys_setsockopt (socket, _level, _option_name, option_value, option_len);
		}

		public static unsafe int setsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, IntPtr option_value, long option_len)
		{
			return setsockopt (socket, level, option_name, (void*) option_value, option_len);
		}

		public static unsafe int setsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, int option_value)
		{
			return setsockopt (socket, level, option_name, &option_value, sizeof (int));
		}

		public static unsafe int setsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, byte[] option_value, long option_len)
		{
			if (option_len > (option_value == null ? 0 : option_value.Length))
				throw new ArgumentOutOfRangeException ("option_len", "option_len > (option_value == null ? 0 : option_value.Length)");
			fixed (byte* ptr = option_value)
				return setsockopt (socket, level, option_name, ptr, option_len);
		}

		public static unsafe int setsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, Timeval option_value)
		{
			var _level = NativeConvert.FromUnixSocketProtocol (level);
			var _option_name = NativeConvert.FromUnixSocketOptionName (option_name);
			return sys_setsockopt_timeval (socket, _level, _option_name, ref option_value);
		}

		public static unsafe int setsockopt (int socket, UnixSocketProtocol level, UnixSocketOptionName option_name, Linger option_value)
		{
			var _level = NativeConvert.FromUnixSocketProtocol (level);
			var _option_name = NativeConvert.FromUnixSocketOptionName (option_name);
			return sys_setsockopt_linger (socket, _level, _option_name, ref option_value);
		}

		// shutdown(2)
		//    int shutdown(int sockfd, int how);
		[DllImport (LIBC, SetLastError=true, 
				EntryPoint="shutdown")]
		static extern int sys_shutdown (int socket, int how);

		public static int shutdown (int socket, ShutdownOption how)
		{
			var _how = NativeConvert.FromShutdownOption (how);
			return sys_shutdown (socket, _how);
		}

		// recv(2)
		//    ssize_t recv(int sockfd, void *buf, size_t len, int flags);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_recv")]
		static extern unsafe long sys_recv (int socket, void *buffer, ulong length, int flags);

		public static unsafe long recv (int socket, void *buffer, ulong length, MessageFlags flags)
		{
			int _flags = NativeConvert.FromMessageFlags (flags);
			return sys_recv (socket, buffer, length, _flags);
		}

		public static unsafe long recv (int socket, IntPtr buffer, ulong length, MessageFlags flags)
		{
			return recv (socket, (void*) buffer, length, flags);
		}

		public static unsafe long recv (int socket, byte[] buffer, ulong length, MessageFlags flags)
		{
			if (length > (ulong) (buffer == null ? 0 : buffer.LongLength))
				throw new ArgumentOutOfRangeException ("length", "length > (buffer == null ? 0 : buffer.LongLength)");
			fixed (byte* ptr = buffer)
				return recv (socket, ptr, length, flags);
		}

		// send(2)
		//    ssize_t send(int sockfd, const void *buf, size_t len, int flags);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_send")]
		static extern unsafe long sys_send (int socket, void *message, ulong length, int flags);

		public static unsafe long send (int socket, void *message, ulong length, MessageFlags flags)
		{
			int _flags = NativeConvert.FromMessageFlags (flags);
			return sys_send (socket, message, length, _flags);
		}

		public static unsafe long send (int socket, IntPtr message, ulong length, MessageFlags flags)
		{
			return send (socket, (void*) message, length, flags);
		}

		public static unsafe long send (int socket, byte[] message, ulong length, MessageFlags flags)
		{
			if (length > (ulong) (message == null ? 0 : message.LongLength))
				throw new ArgumentOutOfRangeException ("length", "length > (message == null ? 0 : message.LongLength)");
			fixed (byte* ptr = message)
				return send (socket, ptr, length, flags);
		}

		// bind(2)
		//    int bind(int sockfd, const struct sockaddr *addr, socklen_t addrlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_bind")]
		static extern unsafe int sys_bind (int socket, _SockaddrHeader* address);

		public static unsafe int bind (int socket, Sockaddr address)
		{
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
			fixed (byte* data = Sockaddr.GetDynamicData (address)) {
				var dyn = new _SockaddrDynamic (address, data, useMaxLength: false);
				return sys_bind (socket, Sockaddr.GetNative (&dyn, addr));
			}
		}

		// connect(2)
		//    int connect(int sockfd, const struct sockaddr *addr, socklen_t addrlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_connect")]
		static extern unsafe int sys_connect (int socket, _SockaddrHeader* address);

		public static unsafe int connect (int socket, Sockaddr address)
		{
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
			fixed (byte* data = Sockaddr.GetDynamicData (address)) {
				var dyn = new _SockaddrDynamic (address, data, useMaxLength: false);
				return sys_connect (socket, Sockaddr.GetNative (&dyn, addr));
			}
		}

		// accept(2)
		//    int accept(int sockfd, struct sockaddr *addr, socklen_t *addrlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_accept")]
		static extern unsafe int sys_accept (int socket, _SockaddrHeader* address);

		public static unsafe int accept (int socket, Sockaddr address)
		{
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
			fixed (byte* data = Sockaddr.GetDynamicData (address)) {
				var dyn = new _SockaddrDynamic (address, data, useMaxLength: true);
				int r = sys_accept (socket, Sockaddr.GetNative (&dyn, addr));
				dyn.Update (address);
				return r;
			}
		}

		// accept4(2)
		//    int accept4(int sockfd, struct sockaddr *addr, socklen_t *addrlen, int flags);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_accept4")]
		static extern unsafe int sys_accept4 (int socket, _SockaddrHeader* address, int flags);

		public static unsafe int accept4 (int socket, Sockaddr address, UnixSocketFlags flags)
		{
			var _flags = NativeConvert.FromUnixSocketFlags (flags);
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
			fixed (byte* data = Sockaddr.GetDynamicData (address)) {
				var dyn = new _SockaddrDynamic (address, data, useMaxLength: true);
				int r = sys_accept4 (socket, Sockaddr.GetNative (&dyn, addr), _flags);
				dyn.Update (address);
				return r;
			}
		}

		// getpeername(2)
		//    int getpeername(int sockfd, struct sockaddr *addr, socklen_t *addrlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_getpeername")]
		static extern unsafe int sys_getpeername (int socket, _SockaddrHeader* address);

		public static unsafe int getpeername (int socket, Sockaddr address)
		{
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
			fixed (byte* data = Sockaddr.GetDynamicData (address)) {
				var dyn = new _SockaddrDynamic (address, data, useMaxLength: true);
				int r = sys_getpeername (socket, Sockaddr.GetNative (&dyn, addr));
				dyn.Update (address);
				return r;
			}
		}

		// getsockname(2)
		//    int getsockname(int sockfd, struct sockaddr *addr, socklen_t *addrlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_getsockname")]
		static extern unsafe int sys_getsockname (int socket, _SockaddrHeader* address);

		public static unsafe int getsockname (int socket, Sockaddr address)
		{
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
			fixed (byte* data = Sockaddr.GetDynamicData (address)) {
				var dyn = new _SockaddrDynamic (address, data, useMaxLength: true);
				int r = sys_getsockname (socket, Sockaddr.GetNative (&dyn, addr));
				dyn.Update (address);
				return r;
			}
		}

		// recvfrom(2)
		//    ssize_t recvfrom(int sockfd, void *buf, size_t len, int flags, struct sockaddr *src_addr, socklen_t *addrlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_recvfrom")]
		static extern unsafe long sys_recvfrom (int socket, void *buffer, ulong length, int flags, _SockaddrHeader* address);

		public static unsafe long recvfrom (int socket, void *buffer, ulong length, MessageFlags flags, Sockaddr address)
		{
			int _flags = NativeConvert.FromMessageFlags (flags);
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
			fixed (byte* data = Sockaddr.GetDynamicData (address)) {
				var dyn = new _SockaddrDynamic (address, data, useMaxLength: true);
				long r = sys_recvfrom (socket, buffer, length, _flags, Sockaddr.GetNative (&dyn, addr));
				dyn.Update (address);
				return r;
			}
		}

		public static unsafe long recvfrom (int socket, IntPtr buffer, ulong length, MessageFlags flags, Sockaddr address)
		{
			return recvfrom (socket, (void*) buffer, length, flags, address);
		}

		public static unsafe long recvfrom (int socket, byte[] buffer, ulong length, MessageFlags flags, Sockaddr address)
		{
			if (length > (ulong) buffer.LongLength)
				throw new ArgumentOutOfRangeException ("length", "length > buffer.LongLength");
			fixed (byte* ptr = buffer)
				return recvfrom (socket, ptr, length, flags, address);
		}

		// sendto(2)
		//    ssize_t sendto(int sockfd, const void *buf, size_t len, int flags, const struct sockaddr *dest_addr, socklen_t addrlen);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_sendto")]
		static extern unsafe long sys_sendto (int socket, void *message, ulong length, int flags, _SockaddrHeader* address);

		public static unsafe long sendto (int socket, void *message, ulong length, MessageFlags flags, Sockaddr address)
		{
			int _flags = NativeConvert.FromMessageFlags (flags);
			fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
			fixed (byte* data = Sockaddr.GetDynamicData (address)) {
				var dyn = new _SockaddrDynamic (address, data, useMaxLength: false);
				return sys_sendto (socket, message, length, _flags, Sockaddr.GetNative (&dyn, addr));
			}
		}

		public static unsafe long sendto (int socket, IntPtr message, ulong length, MessageFlags flags, Sockaddr address)
		{
			return sendto (socket, (void*) message, length, flags, address);
		}

		public static unsafe long sendto (int socket, byte[] message, ulong length, MessageFlags flags, Sockaddr address)
		{
			if (length > (ulong) message.LongLength)
				throw new ArgumentOutOfRangeException ("length", "length > message.LongLength");
			fixed (byte* ptr = message)
				return sendto (socket, ptr, length, flags, address);
		}

		// structure for recvmsg() and sendmsg()
		unsafe struct _Msghdr
		{
			public Iovec* msg_iov;
			public int msg_iovlen;
			public byte* msg_control;
			public long msg_controllen;
			public int msg_flags;

			public _Msghdr (Msghdr message, Iovec* ptr_msg_iov, byte* ptr_msg_control)
			{
				if (message.msg_iovlen > message.msg_iov.Length || message.msg_iovlen < 0)
					throw new ArgumentException ("message.msg_iovlen > message.msg_iov.Length || message.msg_iovlen < 0", "message");
				msg_iov = ptr_msg_iov;
				msg_iovlen = message.msg_iovlen;

				if (message.msg_control == null && message.msg_controllen != 0)
					throw new ArgumentException ("message.msg_control == null && message.msg_controllen != 0", "message");
				if (message.msg_control != null && message.msg_controllen > message.msg_control.Length)
					throw new ArgumentException ("message.msg_controllen > message.msg_control.Length", "message");
				msg_control = ptr_msg_control;
				msg_controllen = message.msg_controllen;

				msg_flags = 0; // msg_flags is only passed out of the kernel
			}

			public void Update (Msghdr message)
			{
				message.msg_controllen = msg_controllen;
				message.msg_flags = NativeConvert.ToMessageFlags (msg_flags);
			}
		}

		// recvmsg(2)
		//    ssize_t recvmsg(int sockfd, struct msghdr *msg, int flags);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_recvmsg")]
		static extern unsafe long sys_recvmsg (int socket, ref _Msghdr message, _SockaddrHeader* msg_name, int flags);

		public static unsafe long recvmsg (int socket, Msghdr message, MessageFlags flags)
		{
			var _flags = NativeConvert.FromMessageFlags (flags);
			var address = message.msg_name;
			fixed (byte* ptr_msg_control = message.msg_control)
			fixed (Iovec* ptr_msg_iov = message.msg_iov) {
				var _message = new _Msghdr (message, ptr_msg_iov, ptr_msg_control);
				long r;
				fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
				fixed (byte* data = Sockaddr.GetDynamicData (address)) {
					var dyn = new _SockaddrDynamic (address, data, useMaxLength: true);
					r = sys_recvmsg (socket, ref _message, Sockaddr.GetNative (&dyn, addr), _flags);
					dyn.Update (address);
				}
				_message.Update (message);
				return r;
			}
		}

		// sendmsg(2)
		//    ssize_t sendmsg(int sockfd, const struct msghdr *msg, int flags);
		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_sendmsg")]
		static extern unsafe long sys_sendmsg (int socket, ref _Msghdr message, _SockaddrHeader* msg_name, int flags);

		public static unsafe long sendmsg (int socket, Msghdr message, MessageFlags flags)
		{
			var _flags = NativeConvert.FromMessageFlags (flags);
			var address = message.msg_name;
			fixed (byte* ptr_msg_control = message.msg_control)
			fixed (Iovec* ptr_msg_iov = message.msg_iov) {
				var _message = new _Msghdr (message, ptr_msg_iov, ptr_msg_control);
				fixed (SockaddrType* addr = &Sockaddr.GetAddress (address).type)
				fixed (byte* data = Sockaddr.GetDynamicData (address)) {
					var dyn = new _SockaddrDynamic (address, data, useMaxLength: false);
					return sys_sendmsg (socket, ref _message, Sockaddr.GetNative (&dyn, addr), _flags);
				}
			}
		}

		// cmsg(3)
		//    struct cmsghdr *CMSG_FIRSTHDR(struct msghdr *msgh);
		//    struct cmsghdr *CMSG_NXTHDR(struct msghdr *msgh, struct cmsghdr *cmsg);
		//    size_t CMSG_ALIGN(size_t length);
		//    size_t CMSG_SPACE(size_t length);
		//    size_t CMSG_LEN(size_t length);
		//    unsigned char *CMSG_DATA(struct cmsghdr *cmsg);

		// Wrapper methods use long offsets into msg_control instead of a
		// struct cmsghdr *cmsg pointer because pointers into a byte[] aren't
		// stable when the array is not pinned.
		// NULL is mapped to -1.

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_CMSG_FIRSTHDR")]
		static extern unsafe long CMSG_FIRSTHDR (byte* msg_control, long msg_controllen);

		public static unsafe long CMSG_FIRSTHDR (Msghdr msgh)
		{
			if (msgh.msg_control == null && msgh.msg_controllen != 0)
				throw new ArgumentException ("msgh.msg_control == null && msgh.msg_controllen != 0", "msgh");
			if (msgh.msg_control != null && msgh.msg_controllen > msgh.msg_control.Length)
				throw new ArgumentException ("msgh.msg_controllen > msgh.msg_control.Length", "msgh");

			fixed (byte* ptr = msgh.msg_control)
				return CMSG_FIRSTHDR (ptr, msgh.msg_controllen);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_CMSG_NXTHDR")]
		static extern unsafe long CMSG_NXTHDR (byte* msg_control, long msg_controllen, long cmsg);

		public static unsafe long CMSG_NXTHDR (Msghdr msgh, long cmsg)
		{
			if (msgh.msg_control == null || msgh.msg_controllen > msgh.msg_control.Length)
				throw new ArgumentException ("msgh.msg_control == null || msgh.msg_controllen > msgh.msg_control.Length", "msgh");
			if (cmsg < 0 || cmsg + Cmsghdr.Size > msgh.msg_controllen)
				throw new ArgumentException ("cmsg offset pointing out of buffer", "cmsg");

			fixed (byte* ptr = msgh.msg_control)
				return CMSG_NXTHDR (ptr, msgh.msg_controllen, cmsg);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_CMSG_DATA")]
		static extern unsafe long CMSG_DATA (byte* msg_control, long msg_controllen, long cmsg);

		public static unsafe long CMSG_DATA (Msghdr msgh, long cmsg)
		{
			if (msgh.msg_control == null || msgh.msg_controllen > msgh.msg_control.Length)
				throw new ArgumentException ("msgh.msg_control == null || msgh.msg_controllen > msgh.msg_control.Length", "msgh");
			if (cmsg < 0 || cmsg + Cmsghdr.Size > msgh.msg_controllen)
				throw new ArgumentException ("cmsg offset pointing out of buffer", "cmsg");

			fixed (byte* ptr = msgh.msg_control)
				return CMSG_DATA (ptr, msgh.msg_controllen, cmsg);
		}

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_CMSG_ALIGN")]
		public static extern ulong CMSG_ALIGN (ulong length);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_CMSG_SPACE")]
		public static extern ulong CMSG_SPACE (ulong length);

		[DllImport (MPH, SetLastError=true, 
				EntryPoint="Mono_Posix_Syscall_CMSG_LEN")]
		public static extern ulong CMSG_LEN (ulong length);

		#endregion
	}

	#endregion
}

// vim: noexpandtab
// Local Variables: 
// tab-width: 4
// c-basic-offset: 4
// indent-tabs-mode: t
// End: 
