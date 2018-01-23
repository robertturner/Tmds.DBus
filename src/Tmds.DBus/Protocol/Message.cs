// Copyright 2006 Alp Toker <alp@atoker.com>
// Copyright 2016 Tom Deseyn <tom.deseyn@gmail.com>
// This software is made available under the MIT License
// See COPYING for details

namespace Tmds.DBus.Protocol
{
    public class Message
    {
        private Header _header;
        private byte[] _body;
        private UnixFd[] _fds;

        public Message(Header header, byte[] body = null, UnixFd[] unixFds = null)
        {
            _header = header;
            _body = body;
            _fds = unixFds;
            _header.Length = _body != null ? (uint)_body.Length : 0;
            _header.NumberOfFds = (uint)(_fds?.Length ?? 0);
        }

        public byte[] Body
        {
            get => _body;
            set
            {
                _body = value;
                if (_header != null)
                    _header.Length = _body != null ? (uint)_body.Length : 0;
            }
        }

        public Header Header
        {
            get => _header;
            set
            {
                _header = value;
                if ((_body != null) && (_header != null))
                    _header.Length = (uint)_body.Length;
            }
        }

        public UnixFd[] UnixFds
        {
            get => _fds;
            set => _fds = value;
        }

    }
}
