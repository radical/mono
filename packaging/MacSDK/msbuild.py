import fileinput

class MSBuild (GitHubPackage):
	def __init__ (self):
		GitHubPackage.__init__ (self, 'mono', 'msbuild', '15',  # note: fix scripts/ci/run-test-mac-sdk.sh when bumping the version number
			revision = '41d9fe2315746d5f8ec11ac0a96c5f1356ad7e28')

	def build (self):
		try:
			self.sh ('./eng/cibuild_bootstrapped_msbuild.sh --host_type mono --configuration Release --skip_tests')
		finally:
			self.sh ('echo "***** Zipping up build logs $PWD"')
			self.sh ('find artifacts -wholename \'*/log/*\' -type f -exec zip msbuild-bin-logs.zip {} \+')

	def install (self):
		# use the bootstrap msbuild as the system might not have one available!
		self.sh ('./artifacts/mono-msbuild/msbuild mono/build/install.proj /p:MonoInstallPrefix=%s /p:Configuration=Release-MONO /p:IgnoreDiffFailure=true' % self.staged_prefix)

	def deploy (self):
		os.symlink('Current', '%s/lib/mono/xbuild/15.0' % self.staged_profile)
		os.symlink('Current', '%s/lib/mono/msbuild/15.0' % self.staged_profile)

MSBuild ()
